using UnityEngine;

/// <summary>
/// Un processo che concorre alla forma del terreno. Riceve l'altezza (distanza dal
/// centro del corpo) prodotta dai layer precedenti nella pipeline e ne restituisce
/// una nuova. Comporre i layer in ordine = sovrapporre i processi geologici nel tempo
/// (forma di base, impatti, vulcanismo, ...). Un layer non sa né gli importa chi viene
/// prima o dopo: lavora solo sull'altezza che riceve.
///
/// Deve essere DETERMINISTICO (stesso input, stesso output) e ECONOMICO: SampleHeight
/// viene chiamato moltissime volte (mesh + walker), quindi ogni layer è sul percorso caldo.
/// </summary>
public abstract class TerrainLayer
{
    /// <param name="unitDir">Direzione unitaria dal centro del corpo (punto sulla sfera).</param>
    /// <param name="height">Altezza prodotta dai layer precedenti.</param>
    /// <returns>Altezza dopo l'applicazione di questo processo.</returns>
    public abstract float Apply(Vector3 unitDir, float height);
}
