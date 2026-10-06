using TMPro;
using UnityEngine;

namespace Lumera.JumpForce
{
    [DefaultExecutionOrder(100)]
    public sealed class JumpForceScore : MonoBehaviour
    {
        public JumpForcePlayer player;
        public TMP_Text scoreText;
        public TMP_Text coinsText;
        [Header("Vitoria")]
        public JumpForceSpawner spawner;
        public JumpForcePlataforma plataforma;
        public JumpForceVictoryFloat victoryFloat;
        [Min(0)] public int victoryReward = 500;
        public bool Completed { get; private set; }
        [Min(0.1f)] public float metersPerPoint = 4;
        [SerializeField] float maximumHeight;
        [SerializeField] int points, coins;
        public float MaximumHeight => maximumHeight;
        public int Points => points;
        public int Coins => coins;

        void Start() => ResetRun();
        void Update()
        {
            if (!player || !player.GameplayEnabled || player.Dead || Completed) return;
            ObserveHeight(player.Body.position.y);
            int finalLevel = spawner ? Mathf.Max(1, spawner.settings.finalLevel) : 450;
            if (points < finalLevel) return;
            Completed = true;
            player.CompleteRun();
            if (victoryFloat) victoryFloat.Begin(player, spawner);
            int reward = Mathf.Max(0, victoryReward);
            coins += reward;
            if (coinsText) coinsText.text = coins.ToString();
            if (plataforma) plataforma.ConcluirPartida(reward);
        }
        public void ObserveHeight(float height)
        {
            float finalHeight = (spawner ? Mathf.Max(1, spawner.settings.finalLevel) : 450) * Mathf.Max(0.1f, metersPerPoint);
            maximumHeight = Mathf.Min(finalHeight, Mathf.Max(maximumHeight, height));
            int next = Mathf.FloorToInt(maximumHeight / Mathf.Max(0.1f, metersPerPoint));
            if (next == points) return;
            points = next;
            if (scoreText) scoreText.text = points.ToString();
        }
        public void CollectCoin()
        {
            if (Completed) return;
            coins++;
            if (coinsText) coinsText.text = coins.ToString();
        }
        public void ResetRun()
        {
            Completed = false;
            maximumHeight = 0;
            points = coins = 0;
            if (scoreText) scoreText.text = "0";
            if (coinsText) coinsText.text = "0";
        }
    }
}