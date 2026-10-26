using UnityEngine;
using UnityEngine.UI;
using LevelLens.Runtime.Core;

namespace LevelLens.Runtime.UI
{
    public class FloatingToggleButton : MonoBehaviour
    {
        [SerializeField] private Button m_Button;

        private void Start()
        {
            if (m_Button != null)
            {
                m_Button.onClick.AddListener(OnClick);
            }
        }

        private void OnClick()
        {
            LevelLensManager.Instance?.OpenReviewPanel();
        }
    }
}
