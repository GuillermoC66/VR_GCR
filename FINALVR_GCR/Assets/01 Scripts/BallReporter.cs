using UnityEngine;

public class BallReporter : MonoBehaviour
{
    [Tooltip("Arrastra aquí el objeto GameManager que tiene el script MatchReferee")]
    public MatchReferee referee;

    [Header("Configuración de Sonidos")]
    public AudioSource audioSource;
    public AudioClip tableHitSound;
    public AudioClip paddleHitSound;
    public AudioClip floorHitSound;
    public AudioClip wallHitSound;

    void OnCollisionEnter(Collision collision)
    {
        string tag = collision.gameObject.tag;
        
        // Calcular la fuerza del impacto (velocidad relativa) para ajustar el volumen
        float impactForce = collision.relativeVelocity.magnitude;
        // Normalizar el volumen entre 0.1 y 1.0 basado en un impacto máximo esperado (ej. 10)
        float hitVolume = Mathf.Clamp(impactForce / 2f, 0.5f, 1f);

        switch (tag)
        {
            case "PlayerPaddle":
                PlaySound(paddleHitSound, hitVolume);
                if (referee != null) referee.OnBallHitPaddle(MatchReferee.Hitter.Player);
                break;
            case "EnemyPaddle":
                PlaySound(paddleHitSound, hitVolume);
                if (referee != null) referee.OnBallHitPaddle(MatchReferee.Hitter.AI);
                break;
            case "TablePlayer":
                PlaySound(tableHitSound, hitVolume);
                if (referee != null) referee.OnBallHitTable(MatchReferee.Hitter.Player);
                break;
            case "TableEnemy":
                PlaySound(tableHitSound, hitVolume);
                if (referee != null) referee.OnBallHitTable(MatchReferee.Hitter.AI);
                break;
            case "Table": // Por si la mesa entera tiene solo la etiqueta "Table"
                PlaySound(tableHitSound, hitVolume);
                break;
            case "Net":
                if (referee != null) referee.OnBallHitNet();
                break;
            case "Floor": 
                PlaySound(floorHitSound, hitVolume);
                if (referee != null) referee.OnBallHitFloor();
                break;
            case "Wall":
                PlaySound(wallHitSound, hitVolume);
                break;
        }
    }

    private void PlaySound(AudioClip clip, float volumeMultiplier = 1f)
    {
        if (audioSource != null && clip != null)
        {
            // Variar ligeramente el pitch (tono) para que no suene repetitivo
            audioSource.pitch = Random.Range(0.9f, 1.1f);
            
            // Aplicamos el volumen del impacto físico multiplicado por el volumen del menú (SFX)
            float finalVolume = volumeMultiplier * MenuManager.sfxVolume;
            audioSource.PlayOneShot(clip, finalVolume);
        }
    }
}