using UnityEngine;
using Unity.WartechStudio.Network;

namespace WartechStudio.BattleMonster
{
    public class Player : NetworkObject
    {
        public GameObject Head;
        public Camera Camera;
        public TextMesh TextMeshNameOnHead;

        public Rigidbody m_BodyRigidbody;

        private float m_MoveSpeed = 30;
        private float m_RotationSpeed = 200;
        private Vector3 m_MoveDirection = Vector3.zero;

        /*
        [Replicated]
        private Quaternion m_HeadRotation { get; set; }

        [ReplicatedOnChange(nameof(m_HeadRotation))]
        void HeadRotationOnChange()
        {
            Head.transform.rotation = m_HeadRotation;
        }
        */
        protected override void Update()
        {
            base.Update();

            if (NetworkManager.Singleton && IsOwner)
            {
                if (Input.GetKeyDown(KeyCode.D))
                    m_MoveDirection += Vector3.right;
                if (Input.GetKeyUp(KeyCode.D))
                    m_MoveDirection -= Vector3.right;
                if (Input.GetKeyDown(KeyCode.A))
                    m_MoveDirection += Vector3.left;
                if (Input.GetKeyUp(KeyCode.A))
                    m_MoveDirection -= Vector3.left;

                if (Input.GetKeyDown(KeyCode.W))
                    m_MoveDirection += Vector3.forward;
                if (Input.GetKeyUp(KeyCode.W))
                    m_MoveDirection -= Vector3.forward;
                if (Input.GetKeyDown(KeyCode.S))
                    m_MoveDirection += Vector3.back;
                if (Input.GetKeyUp(KeyCode.S))
                    m_MoveDirection -= Vector3.back;

                m_BodyRigidbody.MovePosition(transform.position + m_MoveDirection * Time.deltaTime * m_MoveSpeed);

                if (Input.GetKeyDown(KeyCode.Space))
                    m_BodyRigidbody.AddForce(m_MoveDirection + (Vector3.up * 50), ForceMode.Impulse);

                Head.transform.Rotate(0, (Input.GetAxis("Mouse X") * m_RotationSpeed * Time.deltaTime), 0, Space.World);
                /*
                if(m_HeadRotation != Head.transform.rotation)
                   m_HeadRotation = Head.transform.rotation;
                */
            }
        }

        public override void OnAuthorizedUpdate()
        {
            base.OnAuthorizedUpdate();

            TextMeshNameOnHead.text = $"{gameObject.name}-{OwnerId}";
            Camera.gameObject.SetActive(IsOwner);
            //m_BodyRigidbody.detectCollisions = IsOwner;
        }
    }
}
