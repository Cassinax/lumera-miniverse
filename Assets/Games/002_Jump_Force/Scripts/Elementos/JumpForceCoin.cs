using UnityEngine;

namespace Lumera.JumpForce
{
    [DisallowMultipleComponent]
    public sealed class JumpForceCoin : MonoBehaviour
    {
        JumpForceTrailNode node;
        JumpForceScore score;
        bool collected;
        void Awake()
        {
            foreach (var collider in GetComponentsInChildren<Collider>(true)) collider.isTrigger = true;
        }
        public void Configure(JumpForceTrailNode record, JumpForceScore counter)
        {
            node = record;
            score = counter;
            collected = record.coinCollected;
            gameObject.SetActive(record.hasCoin && !collected);
        }
        public void Collect(JumpForcePlayer player)
        {
            if (!player || player.Dead || !player.GameplayEnabled || collected || !gameObject.activeInHierarchy) return;
            var counter = score ? score : player.score;
            if (!counter) return;
            collected = true;
            if (node != null) node.coinCollected = true;
            counter.CollectCoin();
            JumpForceEventos.AvisarMoeda();
            gameObject.SetActive(false);
        }
        void OnTriggerEnter(Collider other) => Collect(other.GetComponentInParent<JumpForcePlayer>());
    }
}