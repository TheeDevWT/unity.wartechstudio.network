using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace WartechStudio.BattleMonster
{
    public class PlayerStartPoint : MonoBehaviour
    {
        public int Index;
        [HideInInspector]
        public bool IsEmpty;

        private void Awake()
        {
            gameObject.transform.localScale = Vector3.zero;
            IsEmpty = true;
        }
    }
}

