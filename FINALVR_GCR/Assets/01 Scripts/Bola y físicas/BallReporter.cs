using UnityEngine;

public class BallReporter : MonoBehaviour
{
    [Tooltip("Arrastra aquí el objeto GameManager que tiene el script MatchReferee")]
    public MatchReferee referee;

    private BallController ballController;
    private float lastPlayerPaddleReportTime = -10f;
    private float lastEnemyPaddleReportTime = -10f;
    private float lastTableBounceReportTime = -10f;
    private float lastNetReportTime = -10f;
    private const float PaddleReportDebounce = 0.15f;
    private const float TableBounceDebounce = 0.08f;
    private const float NetReportDebounce = 0.12f;

    [Header("Configuración de Sonidos")]
    public AudioSource audioSource;
    public AudioClip tableHitSound;
    public AudioClip paddleHitSound;
    public AudioClip netHitSound;
    public AudioClip floorHitSound;
    public AudioClip wallHitSound;

    private void Awake()
    {
        ballController = GetComponent<BallController>();
        if (referee == null)
        {
            referee = FindFirstObjectByType<MatchReferee>();
        }
    }

    /// <summary>
    /// Llamado por PaddleFlickController cuando conecta un golpe arcade por Trigger / proximidad.
    /// </summary>
    public void NotifyPlayerPaddleHit(float strength01 = 0.7f)
    {
        if (Time.time - lastPlayerPaddleReportTime < PaddleReportDebounce) return;
        lastPlayerPaddleReportTime = Time.time;

        float vol = Mathf.Lerp(0.55f, 1f, Mathf.Clamp01(strength01));
        PlaySound(paddleHitSound, vol);
        if (referee != null) referee.OnBallHitPaddle(MatchReferee.Hitter.Player, transform.position);
    }

    /// <summary>
    /// Llamado por RivalAIController / SimplePingPongAI cuando conecta una devolución o saque balístico.
    /// </summary>
    public void NotifyEnemyPaddleHit(float strength01 = 0.7f)
    {
        if (Time.time - lastEnemyPaddleReportTime < PaddleReportDebounce) return;
        lastEnemyPaddleReportTime = Time.time;

        float vol = Mathf.Lerp(0.55f, 1f, Mathf.Clamp01(strength01));
        PlaySound(paddleHitSound, vol);
        if (referee != null) referee.OnBallHitPaddle(MatchReferee.Hitter.AI, transform.position);
    }

    private void OnCollisionEnter(Collision collision)
    {
        GameObject hitObj = collision.gameObject;
        string tag = hitObj.tag;

        float impactForce = collision.relativeVelocity.magnitude;
        float hitVolume = Mathf.Clamp(impactForce / 2f, 0.5f, 1f);
        Vector3 contactPos = collision.contactCount > 0 ? collision.contacts[0].point : transform.position;

        switch (tag)
        {
            case "PlayerPaddle":
                NotifyPlayerPaddleHit(hitVolume);
                break;

            case "EnemyPaddle":
                NotifyEnemyPaddleHit(hitVolume);
                break;

            case "TablePlayer":
                ReportTableBounce(MatchReferee.Hitter.Player, contactPos, hitVolume);
                break;

            case "TableEnemy":
                ReportTableBounce(MatchReferee.Hitter.AI, contactPos, hitVolume);
                break;

            case "Table":
                ReportFallbackTableBounce(contactPos, hitVolume);
                break;

            case "Net":
                ReportNetCollision(hitVolume);
                break;

            case "Floor":
                PlaySound(floorHitSound, hitVolume);
                if (referee != null) referee.OnBallHitFloor();
                break;

            case "Wall":
                PlaySound(wallHitSound, hitVolume);
                if (referee != null) referee.OnBallHitWall();
                break;

            default:
                // Salvaguarda para objetos hijos de PingPongTable sin tag (ej. Collider_Net o Collider_Surface)
                string lowerName = hitObj.name.ToLowerInvariant();
                if (lowerName.Contains("net"))
                {
                    ReportNetCollision(hitVolume);
                }
                else if (lowerName.Contains("collider_surface") || lowerName.Contains("tablesurface"))
                {
                    ReportFallbackTableBounce(contactPos, hitVolume);
                }
                else if (lowerName.Contains("floor") || lowerName.Contains("ground") || lowerName.Contains("suelo"))
                {
                    PlaySound(floorHitSound, hitVolume);
                    if (referee != null) referee.OnBallHitFloor();
                }
                break;
        }
    }

    private void ReportTableBounce(MatchReferee.Hitter side, Vector3 contactPos, float hitVolume)
    {
        if (Time.time - lastTableBounceReportTime < TableBounceDebounce) return;
        lastTableBounceReportTime = Time.time;

        PlaySound(tableHitSound, hitVolume);
        if (ballController != null) ballController.NotifyTableBounce(side, contactPos);
        if (referee != null) referee.OnBallHitTable(side);
    }

    private void ReportFallbackTableBounce(Vector3 contactPos, float hitVolume)
    {
        if (referee == null)
        {
            PlaySound(tableHitSound, hitVolume);
            return;
        }

        // Determinar automáticamente si el contacto ocurrió en la mitad del jugador o del rival
        Vector3 playerCenter = referee.GetPlayerTableSurfaceCenter();
        Vector3 enemyCenter = referee.GetEnemyTableSurfaceCenter();

        if ((playerCenter - enemyCenter).sqrMagnitude > 0.01f)
        {
            float distPlayer = (new Vector2(contactPos.x - playerCenter.x, contactPos.z - playerCenter.z)).sqrMagnitude;
            float distEnemy = (new Vector2(contactPos.x - enemyCenter.x, contactPos.z - enemyCenter.z)).sqrMagnitude;
            MatchReferee.Hitter resolvedSide = distPlayer <= distEnemy ? MatchReferee.Hitter.Player : MatchReferee.Hitter.AI;
            ReportTableBounce(resolvedSide, contactPos, hitVolume);
        }
        else
        {
            PlaySound(tableHitSound, hitVolume);
        }
    }

    private void ReportNetCollision(float hitVolume)
    {
        if (Time.time - lastNetReportTime < NetReportDebounce) return;
        lastNetReportTime = Time.time;

        PlaySound(netHitSound != null ? netHitSound : tableHitSound, hitVolume * 0.65f);
        if (ballController != null) ballController.NotifyNetCollision();
        if (referee != null) referee.OnBallHitNet();
    }

    private void PlaySound(AudioClip clip, float volumeMultiplier = 1f)
    {
        if (audioSource != null && clip != null)
        {
            audioSource.pitch = Random.Range(0.9f, 1.1f);
            float finalVolume = volumeMultiplier * MenuManager.sfxVolume;
            audioSource.PlayOneShot(clip, finalVolume);
        }
    }
}
