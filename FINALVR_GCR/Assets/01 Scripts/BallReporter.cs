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
        if (referee == null) return;

        string tag = collision.gameObject.tag;

        switch (tag)
        {
            case "PlayerPaddle":
                PlaySound(paddleHitSound);
                referee.OnBallHitPaddle(MatchReferee.Hitter.Player);
                break;
            case "EnemyPaddle":
                PlaySound(paddleHitSound);
                referee.OnBallHitPaddle(MatchReferee.Hitter.AI);
                break;
            case "TablePlayer":
                PlaySound(tableHitSound);
                referee.OnBallHitTable(MatchReferee.Hitter.Player);
                break;
            case "TableEnemy":
                PlaySound(tableHitSound);
                referee.OnBallHitTable(MatchReferee.Hitter.AI);
                break;
            case "Net":
                referee.OnBallHitNet();
                break;
            case "Floor": 
                PlaySound(floorHitSound);
                referee.OnBallHitFloor();
                break;
            case "Wall":
                PlaySound(wallHitSound);
                break;
        }
    }

    private void PlaySound(AudioClip clip)
    {
        if (audioSource != null && clip != null)
        {
            // Variar ligeramente el pitch (tono) para que no suene repetitivo
            audioSource.pitch = Random.Range(0.9f, 1.1f);
            audioSource.PlayOneShot(clip);
        }
    }
}