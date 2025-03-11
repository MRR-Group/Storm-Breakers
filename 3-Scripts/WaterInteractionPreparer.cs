using System.Collections.Generic;
using UnityEngine;

namespace StormBreakers
{
    [RequireComponent(typeof(Rigidbody), typeof(WaterInteraction))]
    public class WaterInteractionPreparer : MonoBehaviour
    {
        private Rigidbody rigidBody;
        private WaterInteraction waterInteraction;

        public bool IsPrepared { get; private set; }
        public bool IsFloating { get; private set; }
        
        public HullBuildResult Hull => waterInteraction != null ? waterInteraction.ColliderHull : null;

        private void Awake()
        {
            StopFloating();
        }

        private void OnDisable()
        {
            if (Application.isPlaying)
            {
                StopFloating();
            }
        }
        
        public bool Prepare(IReadOnlyList<HullElement> elements)
        {
            if (!Application.isPlaying)
            {
                return false;
            }

            StopFloating();
            IsPrepared = false;
            IsPrepared = waterInteraction.InitializeFromColliders(elements);
            return IsPrepared;
        }

        public void BeginFloating()
        {
            TryBeginFloating();
        }

        public bool TryBeginFloating()
        {
            if (!Application.isPlaying || !isActiveAndEnabled)
            {
                return false;
            }

            if (IsFloating)
            {
                return true;
            }

            InitializeReferences();

            if (!IsPrepared || !waterInteraction.IsInitialized)
            {
                return false;
            }

            if (!waterInteraction.CanSampleOcean())
            {
                return false;
            }

            waterInteraction.generateWaterForces = true;
            rigidBody.isKinematic = false;
            rigidBody.useGravity = true;
            waterInteraction.enabled = true;
            IsFloating = true;
            rigidBody.WakeUp();
            return true;
        }

        public void StopFloating()
        {
            InitializeReferences();

            waterInteraction.enabled = false;
            waterInteraction.generateWaterForces = false;
            rigidBody.isKinematic = true;
            rigidBody.useGravity = false;
            IsFloating = false;
        }

        private void InitializeReferences()
        {
            if (rigidBody == null)
            {
                rigidBody = GetComponent<Rigidbody>();
            }

            if (waterInteraction == null)
            {
                waterInteraction = GetComponent<WaterInteraction>();
            }
        }
    }
}
