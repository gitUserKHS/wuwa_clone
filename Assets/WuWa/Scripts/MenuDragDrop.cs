using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace WuWa
{
    public enum DragKind { Echo, Weapon }

    /// Draggable item (grid entry or an occupied slot). Spawns a ghost icon
    /// that follows the pointer; DropSlot consumes it on a valid drop, and a
    /// slot-sourced drag released over nothing unequips.
    public class DragItem : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        public DragKind kind;
        public int id = -1;
        public int fromSlot = -1;              // -1 = from grid
        public Sprite ghostSprite;
        public Color ghostColor = Color.white;
        public System.Action onUnequipDrop;    // slot drag dropped on nothing

        public static DragItem Current;
        public static Canvas GhostCanvas;
        [System.NonSerialized] public bool consumed;

        static RectTransform _ghost;

        public void OnBeginDrag(PointerEventData e)
        {
            if (id < 0) return;
            Current = this;
            consumed = false;
            if (GhostCanvas == null) return;
            var go = new GameObject("~dragGhost");
            go.transform.SetParent(GhostCanvas.transform, false);
            _ghost = go.AddComponent<RectTransform>();
            _ghost.sizeDelta = new Vector2(72f, 72f);
            var img = go.AddComponent<Image>();
            img.sprite = ghostSprite;
            img.color = new Color(ghostColor.r, ghostColor.g, ghostColor.b, 0.85f);
            img.raycastTarget = false;
            _ghost.position = e.position;
        }

        public void OnDrag(PointerEventData e)
        {
            if (_ghost != null) _ghost.position = e.position;
        }

        public void OnEndDrag(PointerEventData e)
        {
            if (_ghost != null) { Destroy(_ghost.gameObject); _ghost = null; }
            if (Current == this && !consumed && fromSlot >= 0 && onUnequipDrop != null)
                onUnequipDrop();
            if (Current == this) Current = null;
        }

        /// Drop callbacks refresh the menu, which can destroy the drag source
        /// mid-gesture — OnEndDrag then never fires, so clean the ghost here.
        void OnDestroy()
        {
            if (Current != this) return;
            if (_ghost != null) { Destroy(_ghost.gameObject); _ghost = null; }
            Current = null;
        }

        public static void CancelActive()
        {
            if (_ghost != null) { Object.Destroy(_ghost.gameObject); _ghost = null; }
            Current = null;
        }
    }

    /// Drop target for a specific item kind (an equip slot).
    public class DropSlot : MonoBehaviour, IDropHandler
    {
        public DragKind accepts;
        public int slotIndex;
        public System.Action<DragItem> onDrop;

        public void OnDrop(PointerEventData e)
        {
            var item = DragItem.Current;
            if (item == null || item.kind != accepts || item.id < 0) return;
            item.consumed = true;
            if (onDrop != null) onDrop(item);
        }
    }
}
