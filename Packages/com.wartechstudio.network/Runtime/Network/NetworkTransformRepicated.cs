using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Unity.WartechStudio.Network
{
    public class NetworkTransformRepicated : MonoBehaviour
    {
        public EReplicateTransformFlags ReplicateTransformFlags;
        public NetworkObject ParentNetworkObject;

        int ReplicateMilisecondTime = 100; // 1/10s
        float replicateTransformTimeCount = 0;

        private string m_UniqueIdStr;

        private Coroutine m_SimulateReplicatePositionCoroutine;
        private Coroutine m_SimulateReplicateRotationCoroutine;
        private Coroutine m_SimulateReplicateScaleCoroutine;

        private Vector3 m_replicatePositionTarget;
        private Quaternion m_replicateRotationTarget;
        private Vector3 m_replicateScaleTarget;

        private Vector3 replicatePositionTarget
        {
            get
            {
                return m_replicatePositionTarget;
            }
            set
            {
                m_replicatePositionTarget = value;
                if (ParentNetworkObject.IsOwner)
                    ParentNetworkObject.ReplicatedRpc(false , m_UniqueIdStr + nameof(replicatePositionTarget), m_replicatePositionTarget);
            }
        }
        private Quaternion replicateRotationTarget
        {
            get
            {
                return m_replicateRotationTarget;
            }
            set
            {
                m_replicateRotationTarget = value;
                if (ParentNetworkObject.IsOwner)
                    ParentNetworkObject.ReplicatedRpc(false, m_UniqueIdStr + nameof(replicateRotationTarget), m_replicateRotationTarget);
            }
        }
        private Vector3 replicateScaleTarget
        {
            get
            {
                return m_replicateScaleTarget;
            }
            set
            {
                m_replicateScaleTarget = value;
                if (ParentNetworkObject.IsOwner)
                    ParentNetworkObject.ReplicatedRpc(false, m_UniqueIdStr + nameof(replicateScaleTarget), m_replicateScaleTarget);
            }
        }

        void Update()
        {
            if (!ParentNetworkObject.IsAuthorized || !ParentNetworkObject.IsOwner) return;

            replicateTransformTimeCount += Time.deltaTime;

            if (replicateTransformTimeCount < ReplicateMilisecondTime * 0.001f)
                return;

            if (ReplicateTransformFlags.HasFlag(EReplicateTransformFlags.Position) && replicatePositionTarget != transform.position)
                replicatePositionTarget = transform.position;
            if (ReplicateTransformFlags.HasFlag(EReplicateTransformFlags.Rotation) && replicateRotationTarget != transform.rotation)
                replicateRotationTarget = transform.rotation;
            if (ReplicateTransformFlags.HasFlag(EReplicateTransformFlags.Scale) && replicateScaleTarget != transform.localScale)
                replicateScaleTarget = transform.localScale;

            replicateTransformTimeCount = 0;
        }

        public void Init()
        {
            replicatePositionTarget = transform.position;
            replicateRotationTarget = transform.rotation;
            replicateScaleTarget = transform.localScale;
            if (transform.parent == null)
            {
                m_UniqueIdStr = $"{ParentNetworkObject.ObjectId}-";
            }
            else
            {
                m_UniqueIdStr = $"{transform.name}-{ParentNetworkObject.ObjectId}-";
            }
            
        }

        public void UpdateValue(string valueName,dynamic value)
        {
            if(valueName == m_UniqueIdStr + nameof(replicatePositionTarget))
            {
                replicatePositionTarget = RpcMessageHelpers.GetParameter<Vector3>(value);
                if (m_SimulateReplicatePositionCoroutine != null) StopCoroutine(m_SimulateReplicatePositionCoroutine);
                m_SimulateReplicatePositionCoroutine = StartCoroutine(SimulateReplicatePosition(replicatePositionTarget));
            }

            if (valueName == m_UniqueIdStr + nameof(replicateRotationTarget))
            {
                replicateRotationTarget = RpcMessageHelpers.GetParameter<Quaternion>(value);
                if (m_SimulateReplicateRotationCoroutine != null) StopCoroutine(m_SimulateReplicateRotationCoroutine);
                m_SimulateReplicateRotationCoroutine = StartCoroutine(SimulateReplicateRotation(replicateRotationTarget));
            }

            if (valueName == m_UniqueIdStr + nameof(replicateScaleTarget))
            {
                replicateScaleTarget = RpcMessageHelpers.GetParameter<Vector3>(value);
                if (m_SimulateReplicateScaleCoroutine != null) StopCoroutine(m_SimulateReplicateScaleCoroutine);
                m_SimulateReplicateScaleCoroutine = StartCoroutine(SimulateReplicateScale(replicateScaleTarget));
            }
        }

        IEnumerator SimulateReplicatePosition(Vector3 targetPosition)
        {
            float duration = ReplicateMilisecondTime * 0.001f;
            float time = 0;
            Vector3 startPosition = transform.position;
            while (time < duration)
            {
                transform.position = Vector3.Lerp(startPosition, targetPosition, time / duration);
                time += Time.deltaTime;
                yield return null;
            }
            transform.position = targetPosition;
        }

        IEnumerator SimulateReplicateRotation(Quaternion targetRotation)
        {
            float duration = 0.1f;
            float time = 0;
            Quaternion startRotation = transform.rotation;
            while (time < duration)
            {
                transform.rotation = Quaternion.Lerp(startRotation, targetRotation, time / duration);
                time += Time.deltaTime;
                yield return null;
            }
            transform.rotation = targetRotation;
        }

        IEnumerator SimulateReplicateScale(Vector3 targetScale)
        {
            float duration = 0.1f;
            float time = 0;
            Vector3 startScale = transform.localScale;
            while (time < duration)
            {
                transform.localScale = Vector3.Lerp(startScale, targetScale, time / duration);
                time += Time.deltaTime;
                yield return null;
            }
            transform.localScale = targetScale;
        }
    }
}
