using System;
using System.Collections.Generic;
using BeatSaberPlaylistsLib.Types;
using PlaylistManager.HarmonyPatches;
using PlaylistManager.Interfaces;
using PlaylistManager.Types;
using PlaylistManager.UI;
using PlaylistManager.Utilities;
using UnityEngine;
using Zenject;

namespace PlaylistManager
{
    public class PlaylistDataManager : IInitializable, IDisposable
    {
        private readonly AnnotatedBeatmapLevelCollectionsViewController annotatedBeatmapLevelCollectionsViewController;
        private readonly LevelPackDetailViewController levelPackDetailViewController;
        private readonly LevelFilteringNavigationController levelFilteringNavigationController;
        private readonly FoldersViewController foldersViewController;

        private readonly List<ILevelCollectionUpdater> levelCollectionUpdaters;
        private readonly List<ILevelCollectionsTableUpdater> levelCollectionsTableUpdaters;
        private readonly List<IBeatmapLevelUpdater> beatmapLevelUpdaters;
        private readonly List<IParentManagerUpdater> parentManagerUpdaters;

        public IPlaylist selectedPlaylist;
        public IPlaylistSong selectedPlaylistSong;
        public BeatSaberPlaylistsLib.PlaylistManager parentManager;

        private readonly BeatmapLevelPack emptyBeatmapLevelPack;

        internal PlaylistDataManager(AnnotatedBeatmapLevelCollectionsViewController annotatedBeatmapLevelCollectionsViewController, LevelPackDetailViewController levelPackDetailViewController, LevelFilteringNavigationController levelFilteringNavigationController,
            [InjectOptional] FoldersViewController foldersViewController, List<ILevelCollectionUpdater> levelCollectionUpdaters, List<ILevelCollectionsTableUpdater> levelCollectionsTableUpdaters, List<IBeatmapLevelUpdater> beatmapLevelUpdaters,
            List<IParentManagerUpdater> parentManagerUpdaters)
        {
            this.annotatedBeatmapLevelCollectionsViewController = annotatedBeatmapLevelCollectionsViewController;
            this.levelPackDetailViewController = levelPackDetailViewController;
            this.levelFilteringNavigationController = levelFilteringNavigationController;
            this.foldersViewController = foldersViewController;
            this.parentManagerUpdaters = parentManagerUpdaters;

            this.levelCollectionUpdaters = levelCollectionUpdaters;
            this.levelCollectionsTableUpdaters = levelCollectionsTableUpdaters;
            this.beatmapLevelUpdaters = beatmapLevelUpdaters;

            emptyBeatmapLevelPack = new BeatmapLevelPack(CustomLevelLoader.kCustomLevelPackPrefixId + CustomLevelPathHelper.kCustomLevelsDirectoryName, "Custom Levels", "Custom Levels", BeatSaberMarkupLanguage.Utilities.ImageResources.BlankSprite, BeatSaberMarkupLanguage.Utilities.ImageResources.BlankSprite, PackBuyOption.Default, Array.Empty<BeatmapLevel>(), PlayerSensitivityFlag.Safe);
        }

        public void Initialize()
        {
            levelPackDetailViewController.didActivateEvent += LevelPackDetailViewController_didActivateEvent;
            levelFilteringNavigationController.didSelectBeatmapLevelPackEvent += LevelFilteringNavigationController_didSelectAnnotatedBeatmapLevelCollectionEvent;
            annotatedBeatmapLevelCollectionsViewController.didSelectAnnotatedBeatmapLevelCollectionEvent += AnnotatedBeatmapLevelCollectionsViewController_didSelectAnnotatedBeatmapLevelCollectionEvent;
            LevelCollectionTableView_HandleDidSelectRowEvent.DidSelectLevelEvent += LevelCollectionTableView_DidSelectLevelEvent;

            if (foldersViewController != null)
            {
                foldersViewController.ParentManagerUpdatedEvent += FoldersViewController_ParentManagerUpdatedEvent;
            }

            foreach (var levelCollectionsTableUpdater in levelCollectionsTableUpdaters)
            {
                levelCollectionsTableUpdater.LevelCollectionTableViewUpdatedEvent += LevelCollectionsTableUpdater_LevelCollectionTableViewUpdated;
            }
        }

        public void Dispose()
        {
            levelPackDetailViewController.didActivateEvent -= LevelPackDetailViewController_didActivateEvent;
            levelFilteringNavigationController.didSelectBeatmapLevelPackEvent -= LevelFilteringNavigationController_didSelectAnnotatedBeatmapLevelCollectionEvent;
            annotatedBeatmapLevelCollectionsViewController.didSelectAnnotatedBeatmapLevelCollectionEvent -= AnnotatedBeatmapLevelCollectionsViewController_didSelectAnnotatedBeatmapLevelCollectionEvent;
            LevelCollectionTableView_HandleDidSelectRowEvent.DidSelectLevelEvent -= LevelCollectionTableView_DidSelectLevelEvent;

            if (foldersViewController != null)
            {
                foldersViewController.ParentManagerUpdatedEvent -= FoldersViewController_ParentManagerUpdatedEvent;
            }

            foreach (var levelCollectionsTableUpdater in levelCollectionsTableUpdaters)
            {
                levelCollectionsTableUpdater.LevelCollectionTableViewUpdatedEvent -= LevelCollectionsTableUpdater_LevelCollectionTableViewUpdated;
            }
        }

        private void LevelPackDetailViewController_didActivateEvent(bool firstActivation, bool addedToHierarchy, bool screenSystemEnabling)
        {
            if (annotatedBeatmapLevelCollectionsViewController.isActiveAndEnabled)
            {
                AnnotatedBeatmapLevelCollectionsViewController_didSelectAnnotatedBeatmapLevelCollectionEvent(annotatedBeatmapLevelCollectionsViewController.selectedAnnotatedBeatmapLevelPack);
            }
        }

        private void LevelFilteringNavigationController_didSelectAnnotatedBeatmapLevelCollectionEvent(LevelFilteringNavigationController controller, BeatmapLevelPack annotatedBeatmapLevelCollection, GameObject noDataInfoPrefab, LevelSelectionOptions levelSelectionOptions)
        {
            AnnotatedBeatmapLevelCollectionsViewController_didSelectAnnotatedBeatmapLevelCollectionEvent(annotatedBeatmapLevelCollection);
        }

        private void AnnotatedBeatmapLevelCollectionsViewController_didSelectAnnotatedBeatmapLevelCollectionEvent(BeatmapLevelPack beatmapLevelPack)
        {
            if (beatmapLevelPack is PlaylistLevelPack playlistLevelPack)
            {
                selectedPlaylist = playlistLevelPack.playlist;
                parentManager = GetParentManagerForVisiblePlaylist(playlistLevelPack.playlist);
                Events.RaisePlaylistSelected(playlistLevelPack.playlist, parentManager);
            }
            else
            {
                selectedPlaylist = null;
                parentManager = ReferenceEquals(beatmapLevelPack, emptyBeatmapLevelPack)
                    ? foldersViewController?.CurrentParentManager
                    : null;
            }
            foreach (var levelCollectionUpdater in levelCollectionUpdaters)
            {
                levelCollectionUpdater.LevelCollectionUpdated(beatmapLevelPack, parentManager);
            }
        }

        private void LevelCollectionTableView_DidSelectLevelEvent(BeatmapLevel beatmapLevel)
        {
            if (beatmapLevel is PlaylistLevel playlistLevel)
            {
                Events.RaisePlaylistSongSelected(playlistLevel.playlistSong);
                selectedPlaylistSong = playlistLevel.playlistSong;
            }
            else
            {
                selectedPlaylistSong = null;
            }
            foreach (var beatmapLevelUpdater in beatmapLevelUpdaters)
            {
                beatmapLevelUpdater.BeatmapLevelUpdated(beatmapLevel);
            }
        }

        private void FoldersViewController_ParentManagerUpdatedEvent(BeatSaberPlaylistsLib.PlaylistManager parentManager)
        {
            foreach (var parentManagerUpdater in parentManagerUpdaters)
            {
                parentManagerUpdater.ParentManagerUpdated(parentManager);
            }
        }

        private void LevelCollectionsTableUpdater_LevelCollectionTableViewUpdated(IReadOnlyList<BeatmapLevelPack> annotatedBeatmapLevelCollections, int indexToSelect)
        {
            if (annotatedBeatmapLevelCollections.Count != 0)
            {
                indexToSelect = FindSelectableIndex(annotatedBeatmapLevelCollections, indexToSelect);
                if (indexToSelect >= 0)
                {
                    annotatedBeatmapLevelCollectionsViewController.SetData(annotatedBeatmapLevelCollections, indexToSelect, false);
                    levelFilteringNavigationController.HandleAnnotatedBeatmapLevelCollectionsViewControllerDidSelectAnnotatedBeatmapLevelCollection(annotatedBeatmapLevelCollections[indexToSelect]);
                }
                else
                {
                    // SetData requires an index even when a directory contains only folders.
                    // Clear the programmatic selection immediately so the sole Back tile remains clickable.
                    annotatedBeatmapLevelCollectionsViewController.SetData(annotatedBeatmapLevelCollections, 0, false);
                    ClearSyntheticSelection();

                    selectedPlaylist = null;
                    levelFilteringNavigationController.HandleAnnotatedBeatmapLevelCollectionsViewControllerDidSelectAnnotatedBeatmapLevelCollection(emptyBeatmapLevelPack);
                }
            }
            else
            {
                annotatedBeatmapLevelCollections = new BeatmapLevelPack[] { emptyBeatmapLevelPack };
                annotatedBeatmapLevelCollectionsViewController.SetData(annotatedBeatmapLevelCollections, 0, true);
                levelFilteringNavigationController.HandleAnnotatedBeatmapLevelCollectionsViewControllerDidSelectAnnotatedBeatmapLevelCollection(annotatedBeatmapLevelCollections[0]);
            }
        }

        private BeatSaberPlaylistsLib.PlaylistManager GetParentManagerForVisiblePlaylist(IPlaylist playlist)
        {
            var currentManager = foldersViewController?.CurrentParentManager;
            if (currentManager != null)
            {
                return foldersViewController.TryGetVisibleParent(playlist, out var visibleParentManager)
                    ? visibleParentManager
                    : currentManager;
            }

            return PlaylistLibUtils.playlistManager.GetManagerForPlaylist(playlist);
        }

        private static int FindSelectableIndex(IReadOnlyList<BeatmapLevelPack> collections, int requestedIndex)
        {
            if (requestedIndex >= 0 && requestedIndex < collections.Count && collections[requestedIndex] is not FolderLevelPack)
            {
                return requestedIndex;
            }

            var startIndex = Math.Min(Math.Max(requestedIndex, 0), collections.Count - 1);
            for (var i = startIndex; i >= 0; i--)
            {
                if (collections[i] is not FolderLevelPack)
                {
                    return i;
                }
            }

            for (var i = startIndex + 1; i < collections.Count; i++)
            {
                if (collections[i] is not FolderLevelPack)
                {
                    return i;
                }
            }

            return -1;
        }

        private void ClearSyntheticSelection()
        {
            var gridViewController = annotatedBeatmapLevelCollectionsViewController._annotatedBeatmapLevelCollectionsGridView;
            foreach (var component in gridViewController._gridView.cellsEnumerator)
            {
                if (component is HMUI.SelectableCell selectableCell)
                {
                    selectableCell.SetSelected(false, HMUI.SelectableCell.TransitionType.Instant, gridViewController, false);
                }
            }

            gridViewController._selectedCellIndex = -1;
            annotatedBeatmapLevelCollectionsViewController._selectedItemIndex = -1;
        }
    }
}
