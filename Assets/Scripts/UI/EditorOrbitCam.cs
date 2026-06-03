using UnityEngine;

/// <summary>
/// Camera orbitale dell'editor pianeti: ruota attorno all'origine (il pianeta è centrato lì) col DRAG del
/// tasto DESTRO del mouse (il sinistro resta libero per i pannelli IMGUI), zoom con la rotellina. Semplice,
/// solo per l'editor — niente fisica, niente floating origin.
/// </summary>
public class EditorOrbitCam : MonoBehaviour
{
    public float distance = 1600f;
    public float minDistance = 700f;
    public float maxDistance = 6000f;
    public float yaw = 30f;
    public float pitch = 15f;
    public float rotSpeed = 6f;
    public float zoomSpeed = 2.2f;

    void LateUpdate()
    {
        if (Input.GetMouseButton(1))   // tasto destro: orbita
        {
            yaw += Input.GetAxis("Mouse X") * rotSpeed;
            pitch = Mathf.Clamp(pitch - Input.GetAxis("Mouse Y") * rotSpeed, -85f, 85f);
        }
        float scroll = Input.GetAxis("Mouse ScrollWheel");
        // rotella: zooma il pianeta SOLO se non sei sopra il pannello dei settings (lì scorre la lista).
        if (Mathf.Abs(scroll) > 0.0001f && !PlanetEditor.PointerOverPanel())
            distance = Mathf.Clamp(distance * (1f - scroll * zoomSpeed), minDistance, maxDistance);

        Quaternion rot = Quaternion.Euler(pitch, yaw, 0f);
        transform.position = rot * (Vector3.back * distance);
        transform.rotation = Quaternion.LookRotation(-transform.position, Vector3.up);
    }
}
