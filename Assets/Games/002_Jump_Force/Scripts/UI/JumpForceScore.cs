using TMPro;
using UnityEngine;

namespace Lumera.JumpForce
{
    public sealed class JumpForceScore : MonoBehaviour
    {
        public JumpForcePlayer player;
        public TMP_Text scoreText;
        public TMP_Text coinsText;
        [Min(0.1f)] public float metersPerPoint = 4;
        [SerializeField] float maximumHeight;
        [SerializeField] int points, coins;
        public float MaximumHeight => maximumHeight;
        public int Points => points;
        public int Coins => coins;

        void Start() => ResetRun();
        void Update()
        {
            if (player && player.GameplayEnabled && !player.Dead) ObserveHeight(player.Body.position.y);
        }
        public void ObserveHeight(float height)
        {
            maximumHeight = Mathf.Max(maximumHeight, height);
            int next = Mathf.FloorToInt(maximumHeight / Mathf.Max(0.1f, metersPerPoint));
            if (next == points) return;
            points = next;
            if (scoreText) scoreText.text = points.ToString();
        }
        public void CollectCoin()
        {
            coins++;
            if (coinsText) coinsText.text = coins.ToString();
        }
        public void ResetRun()
        {
            maximumHeight = 0;
            points = coins = 0;
            if (scoreText) scoreText.text = "0";
            if (coinsText) coinsText.text = "0";
        }
    }
}