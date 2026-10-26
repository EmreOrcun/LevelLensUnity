using System;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using LevelLens.Runtime.Core;

namespace LevelLens.Runtime.UI
{
    /// <summary>
    /// Draggable floating arcade button that remains accessible across game scenes.
    /// Tapping toggles LevelLens quick actions ("Test Play", "Report Now").
    /// </summary>
    public class FloatingToggleButton : MonoBehaviour, IDragHandler, IPointerClickHandler
    {
        [SerializeField] private RectTransform m_RectTransform;
        [SerializeField] private Canvas m_Canvas;

        private Vector2 _dragOffset;

        private void Awake()
        {
            if (m_RectTransform == null)
            {
                m_RectTransform = GetComponent<RectTransform>();
            }
            if (m_Canvas == null)
            {
                m_Canvas = GetComponentInParent<Canvas>();
            }
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (m_RectTransform == null || m_Canvas == null) return;

            m_RectTransform.anchoredPosition += eventData.delta / m_Canvas.scaleFactor;
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData.dragging) return;

            var manager = LevelLensManager.Instance;
            if (manager == null) return;

            manager.OpenReviewReportMenu();
        }
    }
}
