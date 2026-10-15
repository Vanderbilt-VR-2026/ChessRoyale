using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ChessRoyale
{
    public sealed class PlayerHealth : MonoBehaviour
    {
        [SerializeField] private float maxHealth = 100f;

        public event Action<float, float> HealthChanged;
        public event Action Died;

        public float MaxHealth => maxHealth;
        public float CurrentHealth { get; private set; }
        public bool IsDead => CurrentHealth <= 0f;

        private void Awake()
        {
            CurrentHealth = maxHealth;
        }

        public void TakeDamage(float amount)
        {
            if (IsDead || amount <= 0f)
                return;

            CurrentHealth = Mathf.Max(0f, CurrentHealth - amount);
            HealthChanged?.Invoke(CurrentHealth, maxHealth);

            if (IsDead)
                Died?.Invoke();
        }

        public void Heal(float amount)
        {
            if (IsDead || amount <= 0f)
                return;

            CurrentHealth = Mathf.Min(maxHealth, CurrentHealth + amount);
            HealthChanged?.Invoke(CurrentHealth, maxHealth);
        }

        public void ResetHealth()
        {
            CurrentHealth = maxHealth;
            HealthChanged?.Invoke(CurrentHealth, maxHealth);
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        // Test keys until gameplay deals real damage: H = take 25 damage, K = kill.
        private void Update()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null)
                return;

            if (keyboard.hKey.wasPressedThisFrame)
                DebugTakeDamage();
            if (keyboard.kKey.wasPressedThisFrame)
                DebugKill();
        }
#endif

        [ContextMenu("Debug/Take 25 Damage")]
        private void DebugTakeDamage() => TakeDamage(25f);

        [ContextMenu("Debug/Kill Player")]
        private void DebugKill() => TakeDamage(CurrentHealth);
    }
}
