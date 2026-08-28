using HMUI;
using UnityEngine;
using UnityEngine.UI;

namespace PlaylistManager.UI
{
    /// <summary>
    /// Adapts HMUI's vertical ScrollView to the centered coordinate system used by
    /// AnnotatedBeatmapLevelCollectionsGridViewAnimator.
    /// </summary>
    [RequireComponent(typeof(EventSystemListener))]
    internal sealed class PlaylistGridScrollView : ScrollView
    {
        private const int VisibleRowCount = 5;

        private AnnotatedBeatmapLevelCollectionsGridView _gridView;
        private AnnotatedBeatmapLevelCollectionsGridViewAnimator _gridViewAnimator;
        private float _zeroPosition;
        private float _endPosition;

        private float GridContentHeight => _contentRectTransform.rect.height;
        private float CurrentPosition => _contentRectTransform.localPosition.y;
        internal int RowCount => _gridView._gridView.rowCount;
        private bool HasOverflow => _gridView._gridView.rowCount > VisibleRowCount;

        internal void Initialize(
            RectTransform viewport,
            RectTransform contentRectTransform,
            Button pageUpButton,
            Button pageDownButton,
            VerticalScrollIndicator verticalScrollIndicator)
        {
            _viewport = viewport;
            _contentRectTransform = contentRectTransform;
            _pageUpButton = pageUpButton;
            _pageDownButton = pageDownButton;
            _verticalScrollIndicator = verticalScrollIndicator;

            _gridView = GetComponent<AnnotatedBeatmapLevelCollectionsGridView>();
            _gridViewAnimator = GetComponent<AnnotatedBeatmapLevelCollectionsGridViewAnimator>();

            _scrollViewDirection = ScrollViewDirection.Vertical;
            _scrollType = ScrollType.FixedCellSize;
            _fixedCellSize = _gridView.cellHeight;
            _joystickScrollSpeed = 30f;
        }

        internal void Open()
        {
            enabled = true;
            RefreshLayout();
        }

        internal void Close()
        {
            _destinationPos = _gridViewAnimator.GetContentYOffset();
            enabled = false;
        }

        internal void RefreshLayout()
        {
            UpdateGridContentSize();

            // Grid content is centered around zero rather than top-aligned like a
            // regular HMUI ScrollView. These are the first and last positions that
            // leave exactly five rows visible.
            _zeroPosition = -(GridContentHeight - _fixedCellSize) / 2f;
            _endPosition = -_zeroPosition - ((VisibleRowCount - 1) * _fixedCellSize);

            SetGridDestinationPosition(_gridViewAnimator.GetContentYOffset());
            RefreshGridButtons();
            UpdateGridScrollIndicator(CurrentPosition);
        }

        internal void UpdateGridContentSize()
        {
            SetContentSize(GridContentHeight);

            var hasOverflow = HasOverflow;
            SetActive(_pageUpButton, hasOverflow);
            SetActive(_pageDownButton, hasOverflow);
            SetActive(_verticalScrollIndicator, hasOverflow);
        }

        internal void RefreshGridButtons()
        {
            if (_pageUpButton != null)
            {
                _pageUpButton.interactable = HasOverflow && _destinationPos > _zeroPosition + 0.001f;
            }

            if (_pageDownButton != null)
            {
                _pageDownButton.interactable = HasOverflow && _destinationPos < _endPosition - 0.001f;
            }
        }

        internal void SetGridDestinationPosition(float value)
        {
            _destinationPos = HasOverflow
                ? Mathf.Clamp(value, _zeroPosition, _endPosition)
                : _zeroPosition;
        }

        internal void UpdateGridScrollIndicator(float _)
        {
            if (_verticalScrollIndicator == null)
            {
                return;
            }

            if (!HasOverflow)
            {
                _verticalScrollIndicator.progress = float.NaN;
                return;
            }

            _verticalScrollIndicator.progress = Mathf.Clamp01(
                (CurrentPosition - _zeroPosition) / (_endPosition - _zeroPosition));
        }

        private static void SetActive(Component component, bool active)
        {
            if (component != null)
            {
                component.gameObject.SetActive(active);
            }
        }
    }
}
