using System;
using System.Collections.Generic;
using SiraUtil.Affinity;
using UnityEngine;

namespace PlaylistManager.AffinityPatches
{
    // TODO: When UI is overlapping other view controllers, blur them.
    internal class AnnotatedBeatmapLevelCollectionsUIPatches : IAffinity
    {
        private readonly MainFlowCoordinator _mainFlowCoordinator;
        private readonly SelectLevelCategoryViewController _selectLevelCategoryViewController;

        private int _originalColumnCount;
        private Vector2 _originalScreenSize;
        private bool _isCustomSongsGridActive;
        private bool _isScreenResized;

        private AnnotatedBeatmapLevelCollectionsUIPatches(MainFlowCoordinator mainFlowCoordinator, SelectLevelCategoryViewController selectLevelCategoryViewController)
        {
            _mainFlowCoordinator = mainFlowCoordinator;
            _selectLevelCategoryViewController = selectLevelCategoryViewController;
        }

        [AffinityPatch(typeof(AnnotatedBeatmapLevelCollectionsGridView), nameof(AnnotatedBeatmapLevelCollectionsGridView.SetData))]
        [AffinityPrefix]
        private void ResizeGrid(AnnotatedBeatmapLevelCollectionsGridView __instance, IReadOnlyList<BeatmapLevelPack> annotatedBeatmapLevelCollections)
        {
            // SetData reinitializes the animator but does not cancel an open/close tween. Close
            // the old layout first so an async cover refresh or folder navigation cannot leave
            // the main screen enlarged or animate the replacement grid with stale dimensions.
            if (_isScreenResized)
            {
                var selectedIndex = __instance._selectedCellIndex;
                if (selectedIndex < 0)
                {
                    __instance._selectedCellIndex = 0;
                }

                __instance.CloseLevelCollection(false);
                __instance._selectedCellIndex = selectedIndex;
            }

            if (_originalColumnCount == default)
            {
                _originalColumnCount = __instance._gridView._columnCount;
            }

            var selectedLevelCategory = _selectLevelCategoryViewController.selectedLevelCategory;
            if (selectedLevelCategory == SelectLevelCategoryViewController.LevelCategory.CustomSongs)
            {
                // Number of columns for max visible row count before it starts clipping with the ground.
                __instance._gridView._columnCount = Math.Max(Mathf.CeilToInt((annotatedBeatmapLevelCollections?.Count ?? 0) / 5f), _originalColumnCount);
                _isCustomSongsGridActive = true;
            }
            else if (selectedLevelCategory == SelectLevelCategoryViewController.LevelCategory.MusicPacks)
            {
                __instance._gridView._columnCount = _originalColumnCount;
                _isCustomSongsGridActive = false;
            }
        }

        [AffinityPatch(typeof(AnnotatedBeatmapLevelCollectionsGridViewAnimator), nameof(AnnotatedBeatmapLevelCollectionsGridViewAnimator.AnimateOpen))]
        private void RecalculateSizeBasedOnColumnCount(AnnotatedBeatmapLevelCollectionsGridViewAnimator __instance, bool animated)
        {
            var x = ((__instance._columnCount - __instance._visibleColumnCount) * 2 + __instance._visibleColumnCount) * __instance._columnWidth;
            if (animated)
            {
                __instance._viewportSizeTween.toValue = new Vector2(x, __instance._viewportSizeTween.toValue.y);
            }
            else
            {
                __instance._viewportTransform.sizeDelta = new Vector2(x, __instance._viewportTransform.sizeDelta.y);
            }

            if (_isCustomSongsGridActive)
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
                var additionalColumns = Math.Max(0, __instance._columnCount - __instance._visibleColumnCount);
                rectTransform.sizeDelta = new Vector2(_originalScreenSize.x + additionalColumns * __instance._columnWidth * 2, _originalScreenSize.y);

                _isScreenResized = true;
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
