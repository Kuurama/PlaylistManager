using System;
using System.Collections.Generic;
using PlaylistManager.Configuration;
using SiraUtil.Affinity;
using UnityEngine;

namespace PlaylistManager.AffinityPatches
{
    // TODO: When UI is overlapping other view controllers, blur them.
    internal class AnnotatedBeatmapLevelCollectionsUIPatches : IAffinity
    {
        internal const int MinimumBaseColumnCount = 5;
        private const int FallbackBaseColumnCount = 8;

        private readonly MainFlowCoordinator _mainFlowCoordinator;
        private readonly AnnotatedBeatmapLevelCollectionsViewController _annotatedBeatmapLevelCollectionsViewController;
        private readonly SelectLevelCategoryViewController _selectLevelCategoryViewController;

        private int _originalColumnCount;
        private int _originalVisibleColumnCount;
        private Vector2 _originalGridSizeDelta;
        private Vector2 _originalGridAnchoredPosition;
        private Vector3 _originalGridLocalPosition;
        private Vector2 _originalScreenSize;
        private bool _originalGridLayoutCaptured;
        private bool _isGridResized;
        private bool _isScreenResized;
        private bool _isScrollableGridOpen;

        private AnnotatedBeatmapLevelCollectionsUIPatches(MainFlowCoordinator mainFlowCoordinator, AnnotatedBeatmapLevelCollectionsViewController annotatedBeatmapLevelCollectionsViewController, SelectLevelCategoryViewController selectLevelCategoryViewController)
        {
            _mainFlowCoordinator = mainFlowCoordinator;
            _annotatedBeatmapLevelCollectionsViewController = annotatedBeatmapLevelCollectionsViewController;
            _selectLevelCategoryViewController = selectLevelCategoryViewController;
        }

        internal int DefaultBaseColumnCount
        {
            get
            {
                CaptureOriginalGridLayout(_annotatedBeatmapLevelCollectionsViewController._annotatedBeatmapLevelCollectionsGridView);
                return _originalColumnCount > 0
                    ? Math.Max(MinimumBaseColumnCount, _originalColumnCount)
                    : FallbackBaseColumnCount;
            }
        }

        internal int ResolveBaseColumnCount(int configuredBaseColumnCount)
        {
            return configuredBaseColumnCount == 0
                ? DefaultBaseColumnCount
                : Math.Max(MinimumBaseColumnCount, configuredBaseColumnCount);
        }

        internal int ConfiguredBaseColumnCount => ResolveBaseColumnCount(PluginConfig.Instance.BaseColumnCount);

        internal bool VerticalScrollingEnabled
        {
            get
            {
                var configuredColumnCount = PluginConfig.Instance.BaseColumnCount;
                return _selectLevelCategoryViewController.selectedLevelCategory == SelectLevelCategoryViewController.LevelCategory.CustomSongs
                    && configuredColumnCount > 0
                    && ResolveBaseColumnCount(configuredColumnCount) < DefaultBaseColumnCount;
            }
        }

        [AffinityPatch(typeof(AnnotatedBeatmapLevelCollectionsGridView), nameof(AnnotatedBeatmapLevelCollectionsGridView.SetData))]
        [AffinityPrefix]
        private void ResizeGrid(AnnotatedBeatmapLevelCollectionsGridView __instance, IReadOnlyList<BeatmapLevelPack> annotatedBeatmapLevelCollections)
        {
            CaptureOriginalGridLayout(__instance);

            var selectedLevelCategory = _selectLevelCategoryViewController.selectedLevelCategory;
            if (selectedLevelCategory == SelectLevelCategoryViewController.LevelCategory.CustomSongs)
            {
                var configuredBaseColumnCount = PluginConfig.Instance.BaseColumnCount;

                // Keep the existing adaptive layout when following the game default. An explicit
                // accessibility override is a hard column count, even when that creates more rows.
                __instance._gridView._columnCount = configuredBaseColumnCount == 0
                    ? Math.Max(Mathf.CeilToInt((annotatedBeatmapLevelCollections?.Count ?? 0) / 5f), _originalColumnCount)
                    : ResolveBaseColumnCount(configuredBaseColumnCount);

                var rectTransform = (RectTransform)__instance.transform;
                if (VerticalScrollingEnabled)
                {
                    // Keep the collapsed strip at the game's safe five-column width,
                    // while the configured count controls the expanded grid and rows.
                    __instance._gridView._visibleColumnCount = Math.Min(
                        MinimumBaseColumnCount,
                        __instance._gridView._columnCount);
                    ApplyScrollableGridTransform(__instance);

                    _isGridResized = false;
                }
                else
                {
                    // Remove one visible column to make room for our buttons.
                    __instance._gridView._visibleColumnCount = Math.Max(1, _originalVisibleColumnCount - 1);
                    rectTransform.sizeDelta = _originalGridSizeDelta - new Vector2(__instance._cellWidth, 0f);
                    rectTransform.anchoredPosition = _originalGridAnchoredPosition - new Vector2(__instance._cellWidth / 2f, 0f);

                    _isGridResized = true;
                }
            }
            else if (selectedLevelCategory == SelectLevelCategoryViewController.LevelCategory.MusicPacks)
            {
                __instance._gridView._columnCount = _originalColumnCount;
                RestoreOriginalGridLayout(__instance);
            }
        }

        private void CaptureOriginalGridLayout(AnnotatedBeatmapLevelCollectionsGridView gridView)
        {
            if (_originalGridLayoutCaptured)
            {
                return;
            }

            var columnCount = gridView._gridView._columnCount;
            if (columnCount > 0)
            {
                _originalColumnCount = columnCount;
                _originalVisibleColumnCount = gridView._gridView._visibleColumnCount;

                var rectTransform = (RectTransform)gridView.transform;
                _originalGridSizeDelta = rectTransform.sizeDelta;
                _originalGridAnchoredPosition = rectTransform.anchoredPosition;
                _originalGridLocalPosition = rectTransform.localPosition;
                _originalGridLayoutCaptured = true;

                Plugin.Log.Debug($"Detected base playlist column count: {_originalColumnCount}");
            }
        }

        private void RestoreOriginalGridLayout(AnnotatedBeatmapLevelCollectionsGridView gridView)
        {
            gridView._gridView._visibleColumnCount = _originalVisibleColumnCount;

            RestoreScrollableGridTransform(gridView);

            _isGridResized = false;
        }

        private void ApplyScrollableGridTransform(AnnotatedBeatmapLevelCollectionsGridView gridView)
        {
            CaptureOriginalGridLayout(gridView);

            var rectTransform = (RectTransform)gridView.transform;
            rectTransform.sizeDelta = new Vector2(-10f, 0f);
            rectTransform.localPosition = new Vector3(
                -10f,
                _originalGridLocalPosition.y,
                _originalGridLocalPosition.z);
        }

        private void RestoreScrollableGridTransform(AnnotatedBeatmapLevelCollectionsGridView gridView)
        {
            if (!_originalGridLayoutCaptured)
            {
                return;
            }

            var rectTransform = (RectTransform)gridView.transform;
            rectTransform.sizeDelta = _originalGridSizeDelta;
            rectTransform.anchoredPosition = _originalGridAnchoredPosition;
        }

        private static void SetViewportX(AnnotatedBeatmapLevelCollectionsGridViewAnimator animator, float x)
        {
            var anchoredPosition = animator._viewportTransform.anchoredPosition;
            animator._viewportTransform.anchoredPosition = new Vector2(x, anchoredPosition.y);
        }

        private static void SetContentX(AnnotatedBeatmapLevelCollectionsGridViewAnimator animator, float x)
        {
            var anchoredPosition = animator._contentTransform.anchoredPosition;
            animator._contentTransform.anchoredPosition = new Vector2(x, anchoredPosition.y);
        }

        private static float GetNativeContentX(AnnotatedBeatmapLevelCollectionsGridViewAnimator animator)
        {
            var halfOverflow = Math.Max(0, animator._columnCount - animator._visibleColumnCount) * 0.5f;
            return Mathf.Clamp(
                ((animator._columnCount - (animator._visibleColumnCount % 2)) * 0.5f) - animator._selectedColumn,
                -halfOverflow,
                halfOverflow) * animator._columnWidth;
        }

        private void PositionClosedScrollableViewport(AnnotatedBeatmapLevelCollectionsGridViewAnimator animator)
        {
            SetViewportX(animator, 0f);
            SetContentX(animator, GetNativeContentX(animator));
        }

        [AffinityPatch(typeof(GridView), nameof(GridView.ReloadData))]
        [AffinityPrefix]
        private void ConstrainScrollableGrid(GridView __instance)
        {
            var playlistGrid = _annotatedBeatmapLevelCollectionsViewController._annotatedBeatmapLevelCollectionsGridView._gridView;
            if (!VerticalScrollingEnabled || !ReferenceEquals(__instance, playlistGrid))
            {
                return;
            }

            var columnCount = ResolveBaseColumnCount(PluginConfig.Instance.BaseColumnCount);
            __instance._columnCount = columnCount;
            __instance._visibleColumnCount = Math.Min(MinimumBaseColumnCount, columnCount);
        }

        [AffinityPatch(typeof(AnnotatedBeatmapLevelCollectionsGridViewAnimator), nameof(AnnotatedBeatmapLevelCollectionsGridViewAnimator.Init))]
        [AffinityPrefix]
        private void ConstrainScrollableAnimator(
            AnnotatedBeatmapLevelCollectionsGridViewAnimator __instance,
            ref int columnCount,
            ref int visibleColumnCount)
        {
            var playlistAnimator = _annotatedBeatmapLevelCollectionsViewController._annotatedBeatmapLevelCollectionsGridView._animator;
            if (!VerticalScrollingEnabled || !ReferenceEquals(__instance, playlistAnimator))
            {
                return;
            }

            columnCount = ConfiguredBaseColumnCount;
            visibleColumnCount = Math.Min(MinimumBaseColumnCount, columnCount);
        }

        [AffinityPatch(typeof(AnnotatedBeatmapLevelCollectionsGridViewAnimator), nameof(AnnotatedBeatmapLevelCollectionsGridViewAnimator.Init))]
        [AffinityPostfix]
        private void PositionScrollableViewportAfterInit(AnnotatedBeatmapLevelCollectionsGridViewAnimator __instance)
        {
            var playlistAnimator = _annotatedBeatmapLevelCollectionsViewController._annotatedBeatmapLevelCollectionsGridView._animator;
            if (!ReferenceEquals(__instance, playlistAnimator))
            {
                return;
            }

            _isScrollableGridOpen = false;
            if (VerticalScrollingEnabled)
            {
                PositionClosedScrollableViewport(__instance);
            }
            else
            {
                SetViewportX(__instance, 0f);
            }
        }

        [AffinityPatch(typeof(AnnotatedBeatmapLevelCollectionsGridViewAnimator), "GetContentXOffset")]
        [AffinityPostfix]
        private void KeepScrollableContentFixed(
            AnnotatedBeatmapLevelCollectionsGridViewAnimator __instance,
            ref float __result)
        {
            var playlistAnimator = _annotatedBeatmapLevelCollectionsViewController._annotatedBeatmapLevelCollectionsGridView._animator;
            if (VerticalScrollingEnabled && _isScrollableGridOpen && ReferenceEquals(__instance, playlistAnimator))
            {
                __result = 0f;
            }
        }

        [AffinityPatch(typeof(AnnotatedBeatmapLevelCollectionsGridViewAnimator), nameof(AnnotatedBeatmapLevelCollectionsGridViewAnimator.ScrollToRowIdxInstant))]
        [AffinityPostfix]
        private void FollowClosedScrollableSelection(AnnotatedBeatmapLevelCollectionsGridViewAnimator __instance)
        {
            var playlistAnimator = _annotatedBeatmapLevelCollectionsViewController._annotatedBeatmapLevelCollectionsGridView._animator;
            if (VerticalScrollingEnabled && !_isScrollableGridOpen && ReferenceEquals(__instance, playlistAnimator))
            {
                PositionClosedScrollableViewport(__instance);
            }
        }

        [AffinityPatch(typeof(AnnotatedBeatmapLevelCollectionsGridViewAnimator), nameof(AnnotatedBeatmapLevelCollectionsGridViewAnimator.AnimateOpen))]
        [AffinityPrefix]
        private void PrepareScrollableViewportForOpen(AnnotatedBeatmapLevelCollectionsGridViewAnimator __instance)
        {
            var playlistAnimator = _annotatedBeatmapLevelCollectionsViewController._annotatedBeatmapLevelCollectionsGridView._animator;
            if (VerticalScrollingEnabled && ReferenceEquals(__instance, playlistAnimator))
            {
                var openViewportX = GetNativeContentX(__instance);
                _isScrollableGridOpen = true;
                SetViewportX(__instance, openViewportX);
                SetContentX(__instance, 0f);
            }
        }

        [AffinityPatch(typeof(AnnotatedBeatmapLevelCollectionsGridViewAnimator), nameof(AnnotatedBeatmapLevelCollectionsGridViewAnimator.AnimateOpen))]
        [AffinityPostfix]
        private void RecalculateSizeBasedOnColumnCount(AnnotatedBeatmapLevelCollectionsGridViewAnimator __instance, bool animated)
        {
            if (VerticalScrollingEnabled)
            {
                // Keep the selected grid content unchanged while retaining the game's
                // normal empty column of open space on each side. The right-hand space
                // houses the vertical controls and is made raycastable by the controller.
                var scrollViewportWidth = (__instance._columnCount + 2) * __instance._columnWidth;
                if (animated)
                {
                    __instance._viewportSizeTween.toValue = new Vector2(
                        scrollViewportWidth,
                        __instance._viewportSizeTween.toValue.y);
                    __instance._contentPositionTween.toValue = new Vector2(
                        0f,
                        __instance._contentPositionTween.toValue.y);
                }
                else
                {
                    __instance._viewportTransform.sizeDelta = new Vector2(
                        scrollViewportWidth,
                        __instance._viewportTransform.sizeDelta.y);
                    __instance._contentTransform.anchoredPosition = new Vector2(
                        0f,
                        __instance._contentTransform.anchoredPosition.y);
                }

                return;
            }

            var x = ((__instance._columnCount - __instance._visibleColumnCount) * 2 + __instance._visibleColumnCount) * __instance._columnWidth;
            if (animated)
            {
                __instance._viewportSizeTween.toValue = new Vector2(x, __instance._viewportSizeTween.toValue.y);
            }
            else
            {
                __instance._viewportTransform.sizeDelta = new Vector2(x, __instance._viewportTransform.sizeDelta.y);
            }

            if (_isGridResized)
            {
                // It would otherwise fly away when setting the Screen size.
                var rectTransform = (RectTransform)_selectLevelCategoryViewController.transform;

                if (rectTransform.anchorMin.x == 0 || rectTransform.anchorMax.x == 0)
                {
                    var localPosition = rectTransform.localPosition;
                    rectTransform.anchorMin = new Vector2(0.5f, rectTransform.anchorMin.y);
                    rectTransform.anchorMax = new Vector2(0.5f, rectTransform.anchorMax.y);
                    rectTransform.localPosition = localPosition;
                }

                rectTransform = (RectTransform)_mainFlowCoordinator._screenSystem.mainScreen.transform;

                if (_originalScreenSize == default)
                {
                    _originalScreenSize = rectTransform.sizeDelta;
                }

                // Resizing Screen is needed to allow the hover hint to be shown when the GridView is larger.
                rectTransform.sizeDelta = new Vector2(_originalScreenSize.x + (__instance._columnCount - __instance._visibleColumnCount - 1) * __instance._columnWidth * 2, _originalScreenSize.y);

                _isScreenResized = true;
            }
        }

        [AffinityPatch(typeof(AnnotatedBeatmapLevelCollectionsGridViewAnimator), nameof(AnnotatedBeatmapLevelCollectionsGridViewAnimator.AnimateClose))]
        [AffinityPrefix]
        private void PrepareScrollableViewportForClose(AnnotatedBeatmapLevelCollectionsGridViewAnimator __instance)
        {
            var playlistAnimator = _annotatedBeatmapLevelCollectionsViewController._annotatedBeatmapLevelCollectionsGridView._animator;
            if (VerticalScrollingEnabled && ReferenceEquals(__instance, playlistAnimator))
            {
                _isScrollableGridOpen = false;
            }
        }

        [AffinityPatch(typeof(AnnotatedBeatmapLevelCollectionsGridViewAnimator), nameof(AnnotatedBeatmapLevelCollectionsGridViewAnimator.AnimateClose))]
        [AffinityPostfix]
        private void RestoreClosedScrollableViewport(AnnotatedBeatmapLevelCollectionsGridViewAnimator __instance, bool animated)
        {
            var playlistAnimator = _annotatedBeatmapLevelCollectionsViewController._annotatedBeatmapLevelCollectionsGridView._animator;
            if (VerticalScrollingEnabled && ReferenceEquals(__instance, playlistAnimator))
            {
                PositionClosedScrollableViewport(__instance);

                if (animated && __instance._contentPositionTween != null)
                {
                    var contentX = __instance._contentTransform.anchoredPosition.x;
                    __instance._contentPositionTween.fromValue = new Vector2(
                        contentX,
                        __instance._contentPositionTween.fromValue.y);
                    __instance._contentPositionTween.toValue = new Vector2(
                        contentX,
                        __instance._contentPositionTween.toValue.y);
                }
            }
        }

        [AffinityPatch(typeof(AnnotatedBeatmapLevelCollectionsGridViewAnimator), nameof(AnnotatedBeatmapLevelCollectionsGridViewAnimator.AnimateClose))]
        private void RestoreScreenSize(AnnotatedBeatmapLevelCollectionsGridViewAnimator __instance)
        {
            if (_isScreenResized)
            {
                var rectTransform = (RectTransform)_mainFlowCoordinator._screenSystem.mainScreen.transform;
                rectTransform.sizeDelta = _originalScreenSize;

                _isScreenResized = false;
            }
        }
    }
}
