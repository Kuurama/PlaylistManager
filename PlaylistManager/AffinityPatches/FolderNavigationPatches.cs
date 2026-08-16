using System.Collections.Generic;
using HMUI;
using PlaylistManager.Types;
using PlaylistManager.UI;
using SiraUtil.Affinity;

namespace PlaylistManager.AffinityPatches
{
    /// <summary>
    /// Routes the native level-pack grid through the playlist-folder navigator.
    /// </summary>
    internal sealed class FolderNavigationPatches : IAffinity
    {
        private readonly FoldersViewController foldersViewController;

        public FolderNavigationPatches(FoldersViewController foldersViewController)
        {
            this.foldersViewController = foldersViewController;
        }

        [AffinityPatch(typeof(LevelFilteringNavigationController), nameof(LevelFilteringNavigationController.ShowPacksInSecondChildController))]
        [AffinityPrefix]
        private void ReplaceCustomSongPacks(LevelFilteringNavigationController __instance, ref IReadOnlyList<BeatmapLevelPack> beatmapLevelPacks)
        {
            if (__instance._selectLevelCategoryViewController.selectedLevelCategory == SelectLevelCategoryViewController.LevelCategory.CustomSongs)
            {
                beatmapLevelPacks = foldersViewController.OpenRoot(
                    beatmapLevelPacks,
                    __instance._levelPackIdToBeSelectedAfterPresent,
                    out var packIdToSelect);
                __instance._levelPackIdToBeSelectedAfterPresent = packIdToSelect;
            }
        }

        [AffinityPatch(typeof(LevelFilteringNavigationController), nameof(LevelFilteringNavigationController.ShowPacksInSecondChildController))]
        [AffinityPostfix]
        private void NormalizeFolderOnlySelection(LevelFilteringNavigationController __instance)
        {
            if (__instance._selectLevelCategoryViewController.selectedLevelCategory == SelectLevelCategoryViewController.LevelCategory.CustomSongs)
            {
                foldersViewController.NormalizeSelectionAfterNativeShow();
            }
        }

        [AffinityPatch(typeof(AnnotatedBeatmapLevelCollectionsGridView), "HandleCellSelectionDidChange")]
        [AffinityPrefix]
        private bool HandleFolderSelection(
            AnnotatedBeatmapLevelCollectionsGridView __instance,
            SelectableCell selectableCell,
            object changeOwner)
        {
            if (object.ReferenceEquals(__instance, changeOwner) || !selectableCell.selected)
            {
                return true;
            }

            if (selectableCell is not AnnotatedBeatmapLevelCollectionCell cell || cell._beatmapLevelPack is not FolderLevelPack folderLevelPack)
            {
                return true;
            }

            // InternalToggle uses the cell itself as changeOwner for an actual click/submit.
            // SetData uses null while selecting its initial index; consume that synthetic
            // selection without navigating automatically.
            if (!object.ReferenceEquals(changeOwner, selectableCell))
            {
                return false;
            }

            // Consuming the folder click also skips the game's normal selection handler,
            // which is responsible for closing the expanded grid. Reset the old animator
            // before SetData publishes the target directory or the replacement cells can
            // inherit its expanded content offset.
            __instance.CloseLevelCollection(false);
            return !foldersViewController.TryOpenFolder(folderLevelPack);
        }

        [AffinityPatch(typeof(AnnotatedBeatmapLevelCollectionsViewController), "get_selectedAnnotatedBeatmapLevelPack")]
        [AffinityPrefix]
        private bool GuardMissingSelection(
            AnnotatedBeatmapLevelCollectionsViewController __instance,
            ref BeatmapLevelPack __result)
        {
            var collections = __instance._annotatedBeatmapLevelCollections;
            if (collections != null && __instance._selectedItemIndex >= 0 && __instance._selectedItemIndex < collections.Count)
            {
                return true;
            }

            __result = null;
            return false;
        }
    }
}
