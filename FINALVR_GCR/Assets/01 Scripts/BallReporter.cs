using UnityEngine;

public class BallReporter : MonoBehaviour
{
    [Tooltip("Arrastra aquí el objeto GameManager que tiene el script MatchReferee")]
    public MatchReferee referee;

    void OnCollisionEnter(Collision collision)
    {
        if (referee == null) return;

        string tag = collision.gameObject.tag;

        switch (tag)
        {
            case "PlayerPaddle":
                referee.OnBallHitPaddle(MatchReferee.Hitter.Player);
                break;
            case "EnemyPaddle":
                referee.OnBallHitPaddle(MatchReferee.Hitter.AI);
                break;
            case "TablePlayer":
                referee.OnBallHitTable(MatchReferee.Hitter.Player);
                break;
            case "TableEnemy":
                referee.OnBallHitTable(MatchReferee.Hitter.AI);
                break;
            case "Net":
                referee.OnBallHitNet();
                break;
            case "Floor": 
                referee.OnBallHitFloor();
                break;
        }
    }
}