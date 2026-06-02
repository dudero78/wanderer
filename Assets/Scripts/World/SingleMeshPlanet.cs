using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// Renderizza un corpo roccioso come MESH SINGOLA per faccia (6 facce cube-sphere), SENZA LOD. A
/// questa scala (corpi ≤ ~1.5 km) basta: niente quadtree → niente cuciture/skirt/popping (difetti
/// inerenti al chunked LOD). Mesh+walker leggono la stessa PlanetTerrain.SampleHeight: una sola verità.
///
/// La build full-res gira su THREAD (le 6 facce sono indipendenti, il calcolo è matematica pura
/// thread-safe), così non c'è freeze di caricamento e si può alzare la risoluzione per i crateri in
/// geometria vera. Intanto si mostra subito un PROXY a bassa risoluzione (pianeta grezzo immediato),
/// sostituito dalla mesh piena appena il thread finisce. Il giocatore sta a terra via SampleHeight,
/// quindi è indipendente dallo stato della mesh.
/// </summary>
public class SingleMeshPlanet : MonoBehaviour
{
    class Face
    {
        public MeshFilter filter;
        public Task task;
        public Vector3[] verts, normals;
        public Vector4[] tangents;
        public Vector2[] uvs;
        public int[] tris;
        public bool applied;
    }
    Face[] faces;
    MeshFilter[] filters;   // persistenti: l'editor ci ricostruisce sopra l'anteprima (RebuildSync)

    /// <summary>Costruisce il pianeta. La pipeline dei layer dev'essere già pronta (terrain.RebuildLayers
    /// chiamato sul main thread): i thread leggono soltanto.</summary>
    public void Build(PlanetTerrain terrain, Material[] faceMats, int fullRes, int proxyRes)
    {
        faces = new Face[6];
        filters = new MeshFilter[6];
        for (int f = 0; f < 6; f++)
        {
            Vector3 up = PlanetMeshBuilder.FaceNormals[f];
            var go = new GameObject("Face" + f);
            go.transform.SetParent(transform, false);
            var mf = go.AddComponent<MeshFilter>();
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = (faceMats != null && f < faceMats.Length) ? faceMats[f] : null;

            // proxy immediato (sincrono, bassa risoluzione): pianeta grezzo subito a schermo
            mf.sharedMesh = PlanetMeshBuilder.BuildFaceMesh(up, terrain, proxyRes);

            var face = new Face { filter = mf };
            filters[f] = mf;
            // full-res su thread: calcola SOLO i dati (niente API Unity)
            face.task = Task.Run(() =>
                PlanetMeshBuilder.ComputeFaceData(up, terrain, fullRes,
                    out face.verts, out face.normals, out face.tangents, out face.uvs, out face.tris));
            faces[f] = face;
        }
    }

    /// <summary>Ricostruzione SINCRONA delle 6 facce a una risoluzione data (per l'anteprima LIVE dell'editor:
    /// cambi una manopola della ricetta → vedi subito). Annulla l'eventuale build su thread in corso così non
    /// sovrascrive dati vecchi. terrain.RebuildLayers dev'essere già chiamato (ApplyRecipe lo fa).</summary>
    public void RebuildSync(PlanetTerrain terrain, int res)
    {
        if (filters == null) return;
        faces = null;   // ferma eventuali swap pendenti dal build iniziale
        for (int f = 0; f < 6; f++)
        {
            if (filters[f] == null) continue;
            var mesh = PlanetMeshBuilder.BuildFaceMesh(PlanetMeshBuilder.FaceNormals[f], terrain, res);
            var old = filters[f].sharedMesh;
            filters[f].sharedMesh = mesh;
            if (old != null) Destroy(old);
        }
    }

    /// <summary>Renderer della faccia f (0..5) in ordine di faccia. Lo usa l'editor per ri-bakeare la
    /// normale-crateri sul materiale GIUSTO (l'ordine dei figli non è garantito).</summary>
    public MeshRenderer FaceRenderer(int f)
        => (filters != null && f >= 0 && f < 6 && filters[f] != null) ? filters[f].GetComponent<MeshRenderer>() : null;

    void Update()
    {
        if (faces == null) return;
        bool allDone = true;
        for (int f = 0; f < 6; f++)
        {
            var face = faces[f];
            if (face.applied) continue;
            if (face.task != null && face.task.IsCompleted)
            {
                if (!face.task.IsFaulted)   // se il thread fallisce, si tiene il proxy (robusto)
                {
                    var mesh = PlanetMeshBuilder.CreateMesh(face.verts, face.normals, face.tangents, face.uvs, face.tris);
                    var old = face.filter.sharedMesh;
                    face.filter.sharedMesh = mesh;
                    if (old != null) Destroy(old);   // libera il proxy
                    face.verts = null; face.normals = null; face.tangents = null; face.uvs = null; face.tris = null;
                }
                face.applied = true;
            }
            else allDone = false;
        }
        if (allDone) faces = null;   // tutto applicato: smetti di pollare
    }
}
