using BeatSaberPlaylistsLib.Types;
using PlaylistManager.Interfaces;
using PlaylistManager.Types;
using PlaylistManager.Utilities;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using Zenject;

namespace PlaylistManager.UI
{
    /// <summary>
    /// Builds the playlist grid for one directory at a time.
    ///
    /// Folder entries are represented by <see cref="FolderLevelPack"/> instances so they can
    /// live alongside playlists in Beat Saber's native pack grid. The actual folder click is
    /// consumed by <c>FolderNavigationPatches</c> before the game treats it like a song pack.
    /// </summary>
    public class FoldersViewController : IInitializable, IDisposable, ILevelCollectionsTableUpdater, IPMRefreshable, ILevelCategoryUpdater
    {
        private const string FolderCoverFileName = "cover.png";
        private const long MaxFolderCoverBytes = 16 * 1024 * 1024;
        private static readonly TimeSpan PendingDeletionTimeout = TimeSpan.FromSeconds(30);

        private readonly AnnotatedBeatmapLevelCollectionsViewController annotatedBeatmapLevelCollectionsViewController;
        private readonly SelectLevelCategoryViewController selectLevelCategoryViewController;
        private readonly PlaylistUpdater playlistUpdater;
        private readonly Dictionary<string, FolderCover> folderCovers = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<IPlaylist, BeatSaberPlaylistsLib.PlaylistManager> visiblePlaylistParents = new();
        private readonly Dictionary<string, DateTime> pendingDeletedPlaylistPaths = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, DateTime> pendingDeletedFolderPaths = new(StringComparer.OrdinalIgnoreCase);

        private IReadOnlyList<BeatmapLevelPack> rootPacks = Array.Empty<BeatmapLevelPack>();
        private IReadOnlyList<BeatmapLevelPack> currentPacks = Array.Empty<BeatmapLevelPack>();
        private BeatSaberPlaylistsLib.PlaylistManager _currentParentManager;
        private Sprite folderIcon;
        private Sprite backIcon;
        private int navigationGeneration;
        private bool disposed;

        public event Action<IReadOnlyList<BeatmapLevelPack>, int> LevelCollectionTableViewUpdatedEvent;
        public event Action<BeatSaberPlaylistsLib.PlaylistManager> ParentManagerUpdatedEvent;

        public BeatSaberPlaylistsLib.PlaylistManager CurrentParentManager
        {
            get => _currentParentManager;
            private set
            {
                if (_currentParentManager == value)
                {
                    return;
                }

                _currentParentManager = value;
                ParentManagerUpdatedEvent?.Invoke(value);
            }
        }

        internal bool CanDeleteCurrentFolder => !disposed && CurrentParentManager?.Parent != null;

        private FoldersViewController(
            AnnotatedBeatmapLevelCollectionsViewController annotatedBeatmapLevelCollectionsViewController,
            SelectLevelCategoryViewController selectLevelCategoryViewController,
            PlaylistUpdater playlistUpdater)
        {
            this.annotatedBeatmapLevelCollectionsViewController = annotatedBeatmapLevelCollectionsViewController;
            this.selectLevelCategoryViewController = selectLevelCategoryViewController;
            this.playlistUpdater = playlistUpdater;
        }

        public async void Initialize()
        {
            playlistUpdater.PlaylistPackRefreshed += HandlePlaylistPackRefreshed;
            backIcon = CreateBackIcon();
            try
            {
                folderIcon = await BeatSaberMarkupLanguage.Utilities.LoadSpriteFromAssemblyAsync("PlaylistManager.Icons.FolderIcon.png");
            }
            catch (Exception exception)
            {
                Plugin.Log.Warn($"Could not load the default folder icon: {exception.Message}");
            }

            if (disposed)
            {
                if (folderIcon != null)
                {
                    DestroyFolderCover(folderIcon);
                    folderIcon = null;
                }

                return;
            }

            // The grid can be opened while this embedded image is still loading. Update its
            // folder cells without disturbing the selected playlist or song.
            if (!disposed && CurrentParentManager != null && IsCustomSongsViewActive() && IsCurrentDirectoryDisplayed())
            {
                RefreshVisibleFolderTilesInPlace();
            }
        }

        public void Dispose()
        {
            disposed = true;
            navigationGeneration++;
            playlistUpdater.PlaylistPackRefreshed -= HandlePlaylistPackRefreshed;

            foreach (var cover in folderCovers.Values)
            {
                if (cover.Sprite != null)
                {
                    DestroyFolderCover(cover.Sprite);
                }
            }

            folderCovers.Clear();
            pendingDeletedPlaylistPaths.Clear();
            pendingDeletedFolderPaths.Clear();

            if (folderIcon != null)
            {
                DestroyFolderCover(folderIcon);
                folderIcon = null;
            }

            if (backIcon != null)
            {
                DestroyFolderCover(backIcon);
                backIcon = null;
            }
        }

        public void LevelCategoryUpdated(SelectLevelCategoryViewController.LevelCategory levelCategory, bool viewControllerActivated)
        {
            if (levelCategory != SelectLevelCategoryViewController.LevelCategory.CustomSongs)
            {
                // Invalidate cover continuations before the shared pack grid starts showing
                // Music Packs or another category.
                navigationGeneration++;
            }
        }

        /// <summary>
        /// Called while Beat Saber is about to display the Custom Songs pack grid.
        /// Keeps the game's own custom packs, but replaces the recursively injected playlist
        /// section with the direct contents of the root Playlists directory.
        /// </summary>
        internal IReadOnlyList<BeatmapLevelPack> OpenRoot(
            IReadOnlyList<BeatmapLevelPack> originalPacks,
            string requestedPackId,
            out string packIdToSelect)
        {
            var currentDirectoryIsDisplayed = CurrentParentManager != null
                && annotatedBeatmapLevelCollectionsViewController.isActiveAndEnabled
                && IsCurrentDirectoryDisplayed();
            packIdToSelect = !string.IsNullOrEmpty(requestedPackId)
                ? requestedPackId
                : currentDirectoryIsDisplayed
                    ? GetSelectedPackId()
                    : null;
            var requestedPackIsInCurrentDirectory = !string.IsNullOrEmpty(requestedPackId)
                && currentPacks.Any(pack => pack?.packID == requestedPackId);
            var keepCurrentDirectory = CurrentParentManager?.Parent != null
                && currentDirectoryIsDisplayed
                && (string.IsNullOrEmpty(requestedPackId) || requestedPackIsInCurrentDirectory);

            rootPacks = (originalPacks ?? Array.Empty<BeatmapLevelPack>())
                .Where(pack => pack is not PlaylistLevelPack && pack is not FolderLevelPack)
                .ToArray();

            if (!keepCurrentDirectory)
            {
                CurrentParentManager = PlaylistLibUtils.playlistManager;
            }

            currentPacks = BuildCurrentDirectoryPacks();
            var desiredPackId = packIdToSelect;
            if (string.IsNullOrEmpty(desiredPackId)
                || !currentPacks.Any(pack => pack is not FolderLevelPack && pack.packID == desiredPackId))
            {
                packIdToSelect = currentPacks.FirstOrDefault(pack => pack is not FolderLevelPack)?.packID;
            }

            StartLoadingVisibleFolderCovers();
            return currentPacks;
        }

        internal void NormalizeSelectionAfterNativeShow()
        {
            if (currentPacks.Count > 0 && currentPacks.All(pack => pack is FolderLevelPack))
            {
                LevelCollectionTableViewUpdatedEvent?.Invoke(currentPacks, -1);
            }
        }

        /// <summary>
        /// Consumes a folder tile and publishes the target directory into the native grid.
        /// </summary>
        internal bool TryOpenFolder(FolderLevelPack folderLevelPack)
        {
            if (folderLevelPack?.TargetManager == null)
            {
                return false;
            }

            CurrentParentManager = folderLevelPack.TargetManager;
            PublishCurrentDirectory();
            return true;
        }

        internal bool TryGetVisibleParent(IPlaylist playlist, out BeatSaberPlaylistsLib.PlaylistManager manager)
            => visiblePlaylistParents.TryGetValue(playlist, out manager);

        /// <summary>
        /// Recycles the selected subfolder, returns to its parent, and immediately removes
        /// it from the visible grid while the playlist library finishes the recycle operation.
        /// </summary>
        internal bool DeleteFolder(BeatSaberPlaylistsLib.PlaylistManager folderManager)
        {
            if (disposed
                || folderManager == null
                || !ReferenceEquals(folderManager, CurrentParentManager)
                || folderManager.Parent == null)
            {
                return false;
            }

            var parentManager = folderManager.Parent;
            var folderPath = NormalizePath(folderManager.PlaylistPath);
            pendingDeletedFolderPaths[folderPath] = DateTime.UtcNow;
            navigationGeneration++;

            try
            {
                parentManager.DeleteChildManager(folderManager, true);
            }
            catch
            {
                pendingDeletedFolderPaths.Remove(folderPath);
                throw;
            }

            RemoveCachedFolderCovers(folderPath);
            CurrentParentManager = parentManager;
            if (IsCustomSongsViewActive())
            {
                PublishCurrentDirectory();
            }

            return true;
        }

        /// <summary>
        /// Removes a just-deleted playlist without immediately re-reading the file that the
        /// playlist library is still moving to the recycle bin on a worker thread.
        /// </summary>
        internal bool RemoveVisiblePlaylist(IPlaylist playlist)
        {
            if (disposed || playlist == null || !IsCustomSongsViewActive())
            {
                return false;
            }

            var removedIndex = -1;
            for (var i = 0; i < currentPacks.Count; i++)
            {
                if (currentPacks[i] is PlaylistLevelPack playlistLevelPack && ReferenceEquals(playlistLevelPack.playlist, playlist))
                {
                    removedIndex = i;
                    break;
                }
            }

            if (removedIndex < 0)
            {
                return false;
            }

            var playlistManager = visiblePlaylistParents.TryGetValue(playlist, out var visibleParent)
                ? visibleParent
                : CurrentParentManager;
            var deletedPlaylistPath = GetPlaylistFilePath(playlistManager, playlist);
            if (!string.IsNullOrEmpty(deletedPlaylistPath))
            {
                pendingDeletedPlaylistPaths[deletedPlaylistPath] = DateTime.UtcNow;
            }

            var remainingPacks = currentPacks.Where((_, index) => index != removedIndex).ToArray();
            currentPacks = remainingPacks;
            visiblePlaylistParents.Remove(playlist);
            playlistUpdater.ShowOnlyPlaylistChangedListeners(remainingPacks);

            var preferredPackId = remainingPacks
                .Take(Math.Min(removedIndex, remainingPacks.Length))
                .LastOrDefault(pack => pack is not FolderLevelPack)?.packID
                ?? remainingPacks.Skip(Math.Min(removedIndex, remainingPacks.Length))
                    .FirstOrDefault(pack => pack is not FolderLevelPack)?.packID;

            LevelCollectionTableViewUpdatedEvent?.Invoke(remainingPacks, FindSelectableIndex(remainingPacks, preferredPackId));
            return true;
        }

        /// <summary>
        /// Refreshes the current manager cache, discovers new direct files, and republishes
        /// the grid only when Custom Songs is still the active category.
        /// </summary>
        internal int RefreshCurrentDirectoryFromDisk()
        {
            var manager = EnsureExistingManager();
            PruneCompletedPendingDeletions();
            SynchronizeDirectChildManagers(manager);
            var directPlaylistCount = PlaylistLibUtils.RefreshDirectPlaylists(manager)
                .Count(playlist => !IsPlaylistPendingDeletion(manager, playlist));
            if (IsCustomSongsViewActive())
            {
                Refresh();
            }

            return directPlaylistCount;
        }

        public void Refresh()
        {
            Refresh(GetSelectedPackId());
        }

        internal void Refresh(string preferredPackId)
        {
            if (disposed || CurrentParentManager == null || !IsCustomSongsViewActive())
            {
                return;
            }

            PublishCurrentDirectory(preferredPackId);
        }

        private void PublishCurrentDirectory(string preferredPackId = null)
        {
            currentPacks = BuildCurrentDirectoryPacks();
            var selectedIndex = FindSelectableIndex(currentPacks, preferredPackId);
            LevelCollectionTableViewUpdatedEvent?.Invoke(currentPacks, selectedIndex);
            StartLoadingVisibleFolderCovers();
        }

        private IReadOnlyList<BeatmapLevelPack> BuildCurrentDirectoryPacks()
        {
            var manager = EnsureExistingManager();
            PruneCompletedPendingDeletions();
            SynchronizeDirectChildManagers(manager);
            var packs = new List<BeatmapLevelPack>();

            if (manager.Parent == null)
            {
                packs.AddRange(rootPacks);
            }
            else
            {
                packs.Add(new FolderLevelPack(manager.Parent, GetBackSprite(), true));
            }

            var childManagers = manager.GetChildManagers()
                .Where(child => child != null
                    && Directory.Exists(child.PlaylistPath)
                    && !IsFolderPendingDeletion(child.PlaylistPath)
                    && !HasPlaylistIgnoreFile(child.PlaylistPath))
                .OrderBy(child => Path.GetFileName(child.PlaylistPath), StringComparer.OrdinalIgnoreCase)
                .ToArray();

            foreach (var childManager in childManagers)
            {
                packs.Add(new FolderLevelPack(
                    childManager,
                    GetFolderSprite(childManager),
                    showNameOnCover: !HasLoadedCustomCover(childManager)));
            }

            var visiblePlaylists = PlaylistLibUtils.TryGetDirectPlaylists(manager)
                .Where(playlist => playlist != null && !IsPlaylistPendingDeletion(manager, playlist))
                .Distinct()
                .ToArray();

            visiblePlaylistParents.Clear();
            foreach (var playlist in visiblePlaylists)
            {
                visiblePlaylistParents[playlist] = manager;
            }

            var playlistPacks = visiblePlaylists
                .Select(playlist => (BeatmapLevelPack)playlist.PlaylistLevelPack)
                .ToArray();

            playlistUpdater.ShowOnlyPlaylistChangedListeners(playlistPacks);
            packs.AddRange(playlistPacks);
            return packs;
        }

        private BeatSaberPlaylistsLib.PlaylistManager EnsureExistingManager()
        {
            var manager = CurrentParentManager ?? PlaylistLibUtils.playlistManager;
            while (manager.Parent != null && !Directory.Exists(manager.PlaylistPath))
            {
                manager = manager.Parent;
            }

            CurrentParentManager = manager;
            return manager;
        }

        private static string GetPlaylistFilePath(BeatSaberPlaylistsLib.PlaylistManager manager, IPlaylist playlist)
        {
            if (manager == null || playlist == null || string.IsNullOrEmpty(playlist.Filename))
            {
                return null;
            }

            var extension = string.IsNullOrWhiteSpace(playlist.SuggestedExtension)
                ? manager.DefaultHandler?.DefaultExtension
                : playlist.SuggestedExtension;
            if (string.IsNullOrWhiteSpace(extension))
            {
                return null;
            }

            return Path.Combine(manager.PlaylistPath, $"{playlist.Filename}.{extension.TrimStart('.')}");
        }

        private bool IsPlaylistPendingDeletion(BeatSaberPlaylistsLib.PlaylistManager manager, IPlaylist playlist)
        {
            var playlistPath = GetPlaylistFilePath(manager, playlist);
            return playlistPath != null && pendingDeletedPlaylistPaths.ContainsKey(playlistPath);
        }

        private void PruneCompletedPendingDeletions()
        {
            var now = DateTime.UtcNow;
            foreach (var pendingDeletion in pendingDeletedPlaylistPaths.ToArray())
            {
                if (!File.Exists(pendingDeletion.Key) || now - pendingDeletion.Value >= PendingDeletionTimeout)
                {
                    pendingDeletedPlaylistPaths.Remove(pendingDeletion.Key);
                }
            }

            foreach (var pendingDeletion in pendingDeletedFolderPaths.ToArray())
            {
                if (!Directory.Exists(pendingDeletion.Key) || now - pendingDeletion.Value >= PendingDeletionTimeout)
                {
                    pendingDeletedFolderPaths.Remove(pendingDeletion.Key);
                }
            }
        }

        private void SynchronizeDirectChildManagers(BeatSaberPlaylistsLib.PlaylistManager manager)
        {
            string[] directDirectories;
            try
            {
                directDirectories = Directory.GetDirectories(manager.PlaylistPath, "*", SearchOption.TopDirectoryOnly);
            }
            catch (Exception exception)
            {
                Plugin.Log.Warn($"Could not enumerate playlist folders in '{manager.PlaylistPath}': {exception.Message}");
                return;
            }

            var existingPaths = new HashSet<string>(
                manager.GetChildManagers().Select(child => NormalizePath(child.PlaylistPath)),
                StringComparer.OrdinalIgnoreCase);

            foreach (var directoryPath in directDirectories)
            {
                var normalizedDirectoryPath = NormalizePath(directoryPath);
                if (pendingDeletedFolderPaths.ContainsKey(normalizedDirectoryPath)
                    || HasPlaylistIgnoreFile(directoryPath)
                    || existingPaths.Contains(normalizedDirectoryPath))
                {
                    continue;
                }

                try
                {
                    manager.CreateChildManager(Path.GetFileName(directoryPath));
                    existingPaths.Add(normalizedDirectoryPath);
                }
                catch (Exception exception)
                {
                    Plugin.Log.Warn($"Could not register playlist folder '{directoryPath}': {exception.Message}");
                }
            }
        }

        private static bool HasPlaylistIgnoreFile(string directoryPath)
        {
            try
            {
                return Directory.EnumerateFiles(directoryPath, "*.plignore", SearchOption.TopDirectoryOnly).Any();
            }
            catch
            {
                // An inaccessible directory should not become an unusable folder tile.
                return true;
            }
        }

        private void HandlePlaylistPackRefreshed(BeatmapLevelPack refreshedPack)
        {
            if (refreshedPack == null || currentPacks.Count == 0)
            {
                return;
            }

            var replacementIndex = -1;
            for (var i = 0; i < currentPacks.Count; i++)
            {
                if (currentPacks[i] is PlaylistLevelPack && currentPacks[i].packID == refreshedPack.packID)
                {
                    replacementIndex = i;
                    break;
                }
            }

            if (replacementIndex < 0)
            {
                return;
            }

            var refreshedPacks = currentPacks.ToArray();
            refreshedPacks[replacementIndex] = refreshedPack;
            currentPacks = refreshedPacks;

            if (refreshedPack is PlaylistLevelPack playlistLevelPack && CurrentParentManager != null)
            {
                visiblePlaylistParents[playlistLevelPack.playlist] = CurrentParentManager;
            }
        }

        private void RefreshVisibleFolderTilesInPlace()
        {
            if (!IsCustomSongsViewActive() || !IsCurrentDirectoryDisplayed())
            {
                return;
            }

            var refreshedPacks = currentPacks
                .Select(pack => pack is FolderLevelPack folderLevelPack
                    ? (BeatmapLevelPack)new FolderLevelPack(
                        folderLevelPack.TargetManager,
                        folderLevelPack.IsBack ? GetBackSprite() : GetFolderSprite(folderLevelPack.TargetManager),
                        folderLevelPack.IsBack,
                        !folderLevelPack.IsBack && !HasLoadedCustomCover(folderLevelPack.TargetManager))
                    : pack)
                .ToArray();

            currentPacks = refreshedPacks;
            annotatedBeatmapLevelCollectionsViewController._annotatedBeatmapLevelCollections = refreshedPacks;

            var gridViewController = annotatedBeatmapLevelCollectionsViewController._annotatedBeatmapLevelCollectionsGridView;
            gridViewController._annotatedBeatmapLevelCollections = refreshedPacks;
            foreach (var component in gridViewController._gridView.cellsEnumerator)
            {
                if (component is not AnnotatedBeatmapLevelCollectionCell cell
                    || cell.cellIndex < 0
                    || cell.cellIndex >= refreshedPacks.Length
                    || refreshedPacks[cell.cellIndex] is not FolderLevelPack refreshedFolder)
                {
                    continue;
                }

                // SetData also refreshes the optional name overlay when a cover.png has
                // just finished loading (or disappeared and the fallback icon is restored).
                cell.SetData(refreshedFolder, false, false, true);
            }
        }

        private void StartLoadingVisibleFolderCovers()
        {
            var manager = CurrentParentManager;
            if (manager == null)
            {
                return;
            }

            var generation = ++navigationGeneration;
            _ = LoadVisibleFolderCoversAsync(manager, generation);
        }

        private async Task LoadVisibleFolderCoversAsync(BeatSaberPlaylistsLib.PlaylistManager manager, int generation)
        {
            var changed = false;
            BeatSaberPlaylistsLib.PlaylistManager[] childManagers;
            try
            {
                childManagers = currentPacks
                    .OfType<FolderLevelPack>()
                    .Where(folder => !folder.IsBack)
                    .Select(folder => folder.TargetManager)
                    .Distinct()
                    .ToArray();
            }
            catch (Exception exception)
            {
                Plugin.Log.Warn($"Could not enumerate playlist folders in '{manager.PlaylistPath}': {exception.Message}");
                return;
            }

            foreach (var childManager in childManagers)
            {
                if (disposed || generation != navigationGeneration)
                {
                    return;
                }

                var coverPath = Path.Combine(childManager.PlaylistPath, FolderCoverFileName);
                if (!File.Exists(coverPath))
                {
                    changed |= RemoveCachedCover(coverPath);
                    continue;
                }

                DateTime lastWriteTimeUtc;
                long fileLength;
                try
                {
                    var fileInfo = new FileInfo(coverPath);
                    lastWriteTimeUtc = fileInfo.LastWriteTimeUtc;
                    fileLength = fileInfo.Length;
                }
                catch (Exception exception)
                {
                    Plugin.Log.Warn($"Could not inspect folder cover '{coverPath}': {exception.Message}");
                    changed |= RemoveCachedCover(coverPath);
                    continue;
                }

                if (fileLength > MaxFolderCoverBytes)
                {
                    Plugin.Log.Warn($"Folder cover '{coverPath}' is larger than {MaxFolderCoverBytes / (1024 * 1024)} MB. Using the default folder icon.");
                    changed |= RemoveCachedCover(coverPath);
                    continue;
                }

                if (folderCovers.TryGetValue(coverPath, out var cachedCover)
                    && cachedCover.LastWriteTimeUtc == lastWriteTimeUtc
                    && cachedCover.Length == fileLength)
                {
                    continue;
                }

                try
                {
                    // Only cover.png files belonging to direct child folders are read here.
                    // BSML performs the texture creation asynchronously for Unity.
                    var imageData = await File.ReadAllBytesAsync(coverPath);
                    if (disposed || generation != navigationGeneration || CurrentParentManager != manager)
                    {
                        return;
                    }

                    var sprite = await BeatSaberMarkupLanguage.Utilities.LoadSpriteAsync(imageData);
                    if (disposed || generation != navigationGeneration || CurrentParentManager != manager)
                    {
                        if (sprite != null)
                        {
                            DestroyFolderCover(sprite);
                        }

                        return;
                    }

                    if (sprite == null)
                    {
                        Plugin.Log.Warn($"Could not decode folder cover '{coverPath}'. Using the default folder icon.");
                        changed |= RemoveCachedCover(coverPath);
                        continue;
                    }

                    if (folderCovers.TryGetValue(coverPath, out cachedCover) && cachedCover.Sprite != null)
                    {
                        DestroyFolderCover(cachedCover.Sprite);
                    }

                    folderCovers[coverPath] = new FolderCover(sprite, lastWriteTimeUtc, fileLength);
                    changed = true;
                }
                catch (Exception exception)
                {
                    Plugin.Log.Warn($"Could not load folder cover '{coverPath}': {exception.Message}");
                    if (!disposed && generation == navigationGeneration && CurrentParentManager == manager)
                    {
                        changed |= RemoveCachedCover(coverPath);
                    }
                }
            }

            if (changed
                && !disposed
                && generation == navigationGeneration
                && CurrentParentManager == manager
                && IsCustomSongsViewActive()
                && IsCurrentDirectoryDisplayed())
            {
                RefreshVisibleFolderTilesInPlace();
            }
        }

        private Sprite GetFolderSprite(BeatSaberPlaylistsLib.PlaylistManager manager)
        {
            var coverPath = Path.Combine(manager.PlaylistPath, FolderCoverFileName);
            return folderCovers.TryGetValue(coverPath, out var cover) && cover.Sprite != null
                ? cover.Sprite
                : GetFallbackFolderSprite();
        }

        private bool HasLoadedCustomCover(BeatSaberPlaylistsLib.PlaylistManager manager)
        {
            var coverPath = Path.Combine(manager.PlaylistPath, FolderCoverFileName);
            return folderCovers.TryGetValue(coverPath, out var cover) && cover.Sprite != null;
        }

        private Sprite GetFallbackFolderSprite()
            => folderIcon != null ? folderIcon : BeatSaberPlaylistsLib.Utilities.DefaultSprite;

        private Sprite GetBackSprite()
            => backIcon != null ? backIcon : GetFallbackFolderSprite();

        private bool RemoveCachedCover(string coverPath)
        {
            if (!folderCovers.TryGetValue(coverPath, out var cachedCover))
            {
                return false;
            }

            folderCovers.Remove(coverPath);
            if (cachedCover.Sprite != null)
            {
                DestroyFolderCover(cachedCover.Sprite);
            }

            return true;
        }

        private void RemoveCachedFolderCovers(string folderPath)
        {
            var folderPrefix = NormalizePath(folderPath) + Path.DirectorySeparatorChar;
            foreach (var coverPath in folderCovers.Keys
                .Where(path => NormalizePath(path).StartsWith(folderPrefix, StringComparison.OrdinalIgnoreCase))
                .ToArray())
            {
                RemoveCachedCover(coverPath);
            }
        }

        private bool IsFolderPendingDeletion(string folderPath)
            => pendingDeletedFolderPaths.ContainsKey(NormalizePath(folderPath));

        private static string NormalizePath(string path)
            => Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        private static void DestroyFolderCover(Sprite sprite)
        {
            var texture = sprite.texture;
            UnityEngine.Object.Destroy(sprite);
            if (texture != null)
            {
                UnityEngine.Object.Destroy(texture);
            }
        }

        private static Sprite CreateBackIcon()
        {
            const int size = 256;
            const int center = size / 2;
            var pixels = new Color32[size * size];
            var white = new Color32(255, 255, 255, 255);

            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    var shaft = x >= 98 && x <= 212 && Math.Abs(y - center) <= 18;
                    var arrowHead = x >= 44 && x <= 112 && Math.Abs(y - center) <= x - 44;
                    if (shaft || arrowHead)
                    {
                        pixels[y * size + x] = white;
                    }
                }
            }

            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "PlaylistManager Back Icon",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            texture.SetPixels32(pixels);
            texture.Apply(false, true);

            var sprite = Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
            sprite.name = "PlaylistManager Back Icon";
            return sprite;
        }

        private bool IsCustomSongsViewActive()
            => !disposed
                && annotatedBeatmapLevelCollectionsViewController.isActiveAndEnabled
                && selectLevelCategoryViewController.selectedLevelCategory == SelectLevelCategoryViewController.LevelCategory.CustomSongs;

        private string GetSelectedPackId()
        {
            var selectedPack = annotatedBeatmapLevelCollectionsViewController.selectedAnnotatedBeatmapLevelPack;
            return selectedPack is FolderLevelPack ? null : selectedPack?.packID;
        }

        private bool IsCurrentDirectoryDisplayed()
        {
            var displayedPacks = annotatedBeatmapLevelCollectionsViewController._annotatedBeatmapLevelCollections;
            if (ReferenceEquals(displayedPacks, currentPacks))
            {
                return true;
            }

            if (displayedPacks == null || displayedPacks.Count != currentPacks.Count)
            {
                return false;
            }

            for (var i = 0; i < displayedPacks.Count; i++)
            {
                if (displayedPacks[i]?.packID != currentPacks[i]?.packID)
                {
                    return false;
                }
            }

            return true;
        }

        private static int FindSelectableIndex(IReadOnlyList<BeatmapLevelPack> packs, string preferredPackId)
        {
            if (!string.IsNullOrEmpty(preferredPackId))
            {
                for (var i = 0; i < packs.Count; i++)
                {
                    if (packs[i] is not FolderLevelPack && packs[i].packID == preferredPackId)
                    {
                        return i;
                    }
                }
            }

            for (var i = 0; i < packs.Count; i++)
            {
                if (packs[i] is not FolderLevelPack)
                {
                    return i;
                }
            }

            return -1;
        }

        private sealed class FolderCover
        {
            internal FolderCover(Sprite sprite, DateTime lastWriteTimeUtc, long length)
            {
                Sprite = sprite;
                LastWriteTimeUtc = lastWriteTimeUtc;
                Length = length;
            }

            internal Sprite Sprite { get; }
            internal DateTime LastWriteTimeUtc { get; }
            internal long Length { get; }
        }
    }
}
