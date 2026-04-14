using UnityEngine;

public class PadelCourtGenerator : MonoBehaviour
{
    [ContextMenu("Generar Cancha Padel Ping Pong")]
    public void GenerarCancha()
    {
        // Medidas oficiales
        float length = 6.10f;  // 6100mm
        float width = 3.206f;  // 3206mm
        float height = 2.05f;  // 2050mm
        float thickness = 0.05f; // Cristal

        GameObject courtBase = new GameObject("Cancha_Padel_Transparente");
        courtBase.transform.position = transform.position; 

        // Generar material de cristal VR-friendly (Transparente, sin Z-Write)
        Material glassMat = new Material(Shader.Find("Standard"));
        glassMat.SetFloat("_Mode", 3); // Transparente
        glassMat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One);
        glassMat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        glassMat.SetInt("_ZWrite", 0);
        glassMat.DisableKeyword("_ALPHATEST_ON");
        glassMat.DisableKeyword("_ALPHABLEND_ON");
        glassMat.EnableKeyword("_ALPHAPREMULTIPLY_ON");
        glassMat.renderQueue = 3000;
        glassMat.color = new Color(0.6f, 0.8f, 1f, 0.15f); // Tono azul cristalino tenue
        glassMat.SetFloat("_Smoothness", 0.95f);

        void CrearPared(string name, Vector3 pos, Vector3 size)
        {
            GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.name = name;
            wall.transform.parent = courtBase.transform;
            wall.transform.localPosition = pos;
            wall.transform.localScale = size;
            wall.GetComponent<MeshRenderer>().material = glassMat;
            
            // Asignar el Tag "Wall" para que tu PingPongBall lo reconozca en OnCollisionEnter
            wall.tag = "Wall"; 
        }

        // Crear las 4 paredes formando el cubo
        CrearPared("Pared_Frontal", new Vector3(0, height / 2, length / 2), new Vector3(width, height, thickness));
        CrearPared("Pared_Trasera", new Vector3(0, height / 2, -length / 2), new Vector3(width, height, thickness));
        CrearPared("Pared_LateralIzq", new Vector3(-width / 2, height / 2, 0), new Vector3(thickness, height, length));
        CrearPared("Pared_LateralDer", new Vector3(width / 2, height / 2, 0), new Vector3(thickness, height, length));

        Debug.Log($"Cancha generada con medidas: {length}m x {width}m x {height}m");
    }
}