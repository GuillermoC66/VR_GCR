using UnityEngine;
using TMPro;

public class MatchReferee : MonoBehaviour
{
    [Header("UI Marcador")]
    public TextMeshProUGUI scoreText;   // Asigna un texto para "0 - 0"
    public TextMeshProUGUI statusText;  // Asigna un texto para mostrar "Falta", "Punto", etc.

    [Header("Reglas Oficiales")]
    public int pointsToWin = 11;
    public int pointsToWinBy = 2;

    // Estado del Marcador
    private int playerScore = 0;
    private int aiScore = 0;
    private bool isPlayerTurnToServe = true;
    private bool isServePhase = true; 

    // Estado de la Pelota
    public enum Hitter { None, Player, AI }
    private Hitter lastHitter = Hitter.None;
    private int bouncesOnPlayerSide = 0;
    private int bouncesOnAISide = 0;
    private bool touchedNet = false;

    void Start()
    {
        UpdateUI();
    }

    // --- LÓGICA DE PALAS ---
    public void OnBallHitPaddle(Hitter hitter)
    {
        if (hitter == Hitter.Player)
        {
            if (lastHitter == Hitter.Player) { AwardPoint(Hitter.AI, "Falta: Doble Toque"); return; }
            if (lastHitter == Hitter.AI && bouncesOnPlayerSide == 0) { AwardPoint(Hitter.AI, "Falta: Volea no permitida"); return; }
            if (isServePhase && !isPlayerTurnToServe) { AwardPoint(Hitter.AI, "Falta: Era saque de la IA"); return; }
            
            lastHitter = Hitter.Player;
        }
        else if (hitter == Hitter.AI)
        {
            if (lastHitter == Hitter.AI) { AwardPoint(Hitter.Player, "Falta IA: Doble Toque"); return; }
            if (lastHitter == Hitter.Player && bouncesOnAISide == 0) { AwardPoint(Hitter.Player, "Falta IA: Volea"); return; }
            if (isServePhase && isPlayerTurnToServe) { AwardPoint(Hitter.Player, "Falta IA: Era tu saque"); return; }
            
            lastHitter = Hitter.AI;
        }

        // Al golpear válido, se resetean los rebotes para evaluar la respuesta
        bouncesOnPlayerSide = 0;
        bouncesOnAISide = 0;
        touchedNet = false;
        isServePhase = false; 
    }

    // --- LÓGICA DE MESA ---
    public void OnBallHitTable(Hitter tableSide)
    {
        if (tableSide == Hitter.Player)
        {
            bouncesOnPlayerSide++;
            if (lastHitter == Hitter.None) { AwardPoint(Hitter.AI, "Pelota cayó sin sacar"); return; }
            
            if (lastHitter == Hitter.Player) 
            {
                if (bouncesOnPlayerSide > 1) AwardPoint(Hitter.AI, "Falta: Doble rebote en tu lado (Mal Saque)");
            }
            else if (lastHitter == Hitter.AI)
            {
                if (bouncesOnPlayerSide > 1) AwardPoint(Hitter.AI, "¡Punto para la IA!"); // No llegaste a responder
            }
        }
        else if (tableSide == Hitter.AI)
        {
            bouncesOnAISide++;
            if (lastHitter == Hitter.None) { AwardPoint(Hitter.Player, "Pelota cayó sin sacar"); return; }

            if (lastHitter == Hitter.AI)
            {
                if (bouncesOnAISide > 1) AwardPoint(Hitter.Player, "Falta IA: Doble rebote en su lado");
            }
            else if (lastHitter == Hitter.Player)
            {
                // Regla del "Let": Saque válido pero rozó la red
                if (isServePhase && bouncesOnPlayerSide == 1 && bouncesOnAISide == 1 && touchedNet)
                {
                    CallLet("LET: La pelota tocó la red. Repite saque."); return; 
                }

                if (bouncesOnAISide > 1) AwardPoint(Hitter.Player, "¡Punto para ti!"); // IA no llegó a responder
            }
        }
    }

    // --- LÓGICA DE ENTORNO ---
    public void OnBallHitNet() { touchedNet = true; }

    public void OnBallHitFloor()
    {
        if (lastHitter == Hitter.Player) AwardPoint(Hitter.AI, "Lanzaste la pelota fuera");
        else if (lastHitter == Hitter.AI) AwardPoint(Hitter.Player, "IA lanzó la pelota fuera");
        else CallLet("La pelota cayó al suelo. Saca de nuevo.");
    }

    // --- SISTEMA DE PUNTOS Y SAQUES ---
    private void CallLet(string reason)
    {
        statusText.text = reason;
        ResetPlayState();
    }

    private void AwardPoint(Hitter winner, string reason)
    {
        if (winner == Hitter.Player) playerScore++;
        else aiScore++;

        statusText.text = reason;

        CheckWinConditionAndServe();
        UpdateUI();
        ResetPlayState();
    }

    private void CheckWinConditionAndServe()
    {
        // Verificar ganador del juego
        if (playerScore >= pointsToWin && (playerScore - aiScore) >= pointsToWinBy) {
            statusText.text = "¡GANASTE EL JUEGO!"; return;
        }
        else if (aiScore >= pointsToWin && (aiScore - playerScore) >= pointsToWinBy) {
            statusText.text = "¡IA GANA EL JUEGO!"; return;
        }

        // Regla PongFit: Alternar saques cada 2 puntos (o cada 1 si hay Deuce 10-10)
        int totalPoints = playerScore + aiScore;
        if (playerScore >= 10 && aiScore >= 10)
        {
            isPlayerTurnToServe = (totalPoints % 2 == 0);
        }
        else
        {
            isPlayerTurnToServe = ((totalPoints / 2) % 2 == 0);
        }
    }

    private void ResetPlayState()
    {
        lastHitter = Hitter.None;
        bouncesOnPlayerSide = 0;
        bouncesOnAISide = 0;
        touchedNet = false;
        isServePhase = true;

        // Opcional: Detener la pelota físicamente para preparar el siguiente saque
        // ballRigidbody.linearVelocity = Vector3.zero;
    }

    private void UpdateUI()
    {
        scoreText.text = $"{playerScore} - {aiScore}";
        string turnInfo = isPlayerTurnToServe ? "Tu turno de sacar" : "Turno de la IA";
        statusText.text = statusText.text + "\n" + turnInfo;
    }
}