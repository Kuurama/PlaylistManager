using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using BeatSaberPlaylistsLib;
using BeatSaberPlaylistsLib.Blist;
using BeatSaberPlaylistsLib.Legacy;
using BeatSaberPlaylistsLib.Types;
using JetBrains.Annotations;
using PlaylistManager.Configuration;
using UnityEngine;

namespace PlaylistManager.Utilities
{
    public static class PlaylistLibUtils
    {
        private const string ICON_PATH = "PlaylistManager.Icons.DefaultIcon.png";
        private const string EASTER_EGG_URL = "https://raw.githubusercontent.com/rithik-b/PlaylistManager/master/img/easteregg.bplist";

        public static BeatSaberPlaylistsLib.PlaylistManager playlistManager
        {
            get
            {
                return BeatSaberPlaylistsLib.PlaylistManager.DefaultManager;
            }
        }

        public static IPlaylist CreatePlaylistWithConfig(string playlistName, BeatSaberPlaylistsLib.PlaylistManager playlistManager)
        {
            var playlistAuthorName = PluginConfig.Instance.AuthorName;
            var easterEgg = playlistAuthorName.IndexOf("BINTER", StringComparison.OrdinalIgnoreCase) >= 0 && playlistName.IndexOf("TECH", StringComparison.OrdinalIgnoreCase) >= 0 && PluginConfig.Instance.EasterEggs;
            return CreatePlaylist(playlistName, playlistAuthorName, playlistManager, !PluginConfig.Instance.DefaultImageDisabled, PluginConfig.Instance.DefaultAllowDuplicates, easterEgg);
        }

        public static IPlaylist CreatePlaylist(string playlistName, string playlistAuthorName, BeatSaberPlaylistsLib.PlaylistManager playlistManager, bool defaultCover = true,
            bool allowDups = true, bool easterEgg = false)
        {
            var playlist = playlistManager.CreatePlaylist("", playlistName, playlistAuthorName, "");

            if (defaultCover)
            {
                using (var imageStream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ICON_PATH))
                {
                    playlist.SetCover(imageStream);
                }
            }


            if (!allowDups)
            {
                playlist.AllowDuplicates = false;
            }

            if (easterEgg)
            {
                playlist.SetCustomData("syncURL", EASTER_EGG_URL);
            }

            playlist.RaisePlaylistChanged();
            playlistManager.StorePlaylist(playlist);
            PlaylistLibUtils.playlistManager.RequestRefresh("PlaylistManager (plugin)");
            return playlist;
        }

        public static string GetIdentifierForPlaylistSong(IPlaylistSong playlistSong)
        {
            if (playlistSong.Identifiers.HasFlag(Identifier.Hash))
            {
                return playlistSong.Hash;
            }
            if (playlistSong.Identifiers.HasFlag(Identifier.Key))
            {
                return playlistSong.Key;
            }
            if (playlistSong.Identifiers.HasFlag(Identifier.LevelId))
            {
                return playlistSong.LevelId;
            }
            return "";
        }

        public static List<IPlaylistSong> GetMissingSongs(IPlaylist playlist, HashSet<string> ownedHashes = null)
        {
            if (playlist != null)
            {
                return playlist.Where(s => s.BeatmapLevel == null && !(ownedHashes?.Contains(s.Hash) ?? false)).Distinct(IPlaylistSongComparer<IPlaylistSong>.Default).ToList();
            }
            return new List<IPlaylistSong>();
        }

        public static IPlaylist[] TryGetAllPlaylists()
        {
            var playlists = playlistManager.GetAllPlaylists(true, out AggregateException ex);
            if (ex is not null)
            {
                Plugin.Log.Error(ex.Message);
                foreach (var e in ex.InnerExceptions)
                {
                    Plugin.Log.Error(e.ToString());
                }
            }

            return playlists;
        }

        /// <summary>
        /// Loads only playlist files physically present in <paramref name="manager"/>'s
        /// directory. BeatSaberPlaylistsLib's GetAllPlaylists(false) still searches child
        /// manager caches, which can make a playlist from a sub-folder appear here when an
        /// unrelated file has the same name.
        /// </summary>
        public static IPlaylist[] TryGetDirectPlaylists(BeatSaberPlaylistsLib.PlaylistManager manager)
        {
            if (manager == null || string.IsNullOrEmpty(manager.PlaylistPath))
            {
                return Array.Empty<IPlaylist>();
            }

            var candidates = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            var exceptions = new List<Exception>();

            try
            {
                foreach (var path in Directory.EnumerateFiles(manager.PlaylistPath, "*", SearchOption.TopDirectoryOnly))
                {
                    try
                    {
                        var extension = Path.GetExtension(path);
                        var fileName = Path.GetFileNameWithoutExtension(path);
                        if (string.IsNullOrEmpty(fileName) || manager.GetHandlerForExtension(extension) == null)
                        {
                            continue;
                        }

                        if (!candidates.TryGetValue(fileName, out var matchingFiles))
                        {
                            matchingFiles = new List<string>();
                            candidates[fileName] = matchingFiles;
                        }

                        matchingFiles.Add(path);
                    }
                    catch (Exception exception)
                    {
                        exceptions.Add(exception);
                    }
                }
            }
            catch (Exception exception)
            {
                exceptions.Add(exception);
            }

            var directPlaylists = new List<IPlaylist>(candidates.Count);
            foreach (var candidate in candidates)
            {
                if (candidate.Value.Count > 1)
                {
                    Plugin.Log.Warn($"Multiple playlist files named '{candidate.Key}' exist in '{manager.PlaylistPath}'. Only one can be displayed by BeatSaberPlaylistsLib.");
                }

                try
                {
                    // A manager's cache is local, and searchChildren:false is essential here.
                    var playlist = manager.GetPlaylist(candidate.Key, false);
                    if (playlist != null)
                    {
                        directPlaylists.Add(playlist);
                    }
                }
                catch (Exception exception)
                {
                    exceptions.Add(exception);
                }
            }

            if (exceptions.Count > 0)
            {
                var aggregateException = new AggregateException(exceptions);
                Plugin.Log.Error($"Some playlists in '{manager.PlaylistPath}' could not be loaded: {aggregateException.Message}");
                foreach (var exception in aggregateException.InnerExceptions)
                {
                    Plugin.Log.Error(exception.ToString());
                }
            }

            return directPlaylists.ToArray();
        }

        /// <summary>
        /// Reloads only the playlist objects represented by direct files in this manager.
        /// This deliberately avoids PlaylistManager.RefreshPlaylists(false), whose internal
        /// GetAllPlaylists call can still resolve same-named playlists from child caches.
        /// </summary>
        public static IPlaylist[] RefreshDirectPlaylists(BeatSaberPlaylistsLib.PlaylistManager manager)
        {
            var directPlaylists = TryGetDirectPlaylists(manager);
            string[] directFiles;
            try
            {
                directFiles = Directory.GetFiles(manager.PlaylistPath, "*", SearchOption.TopDirectoryOnly);
            }
            catch (Exception exception)
            {
                Plugin.Log.Error($"Could not enumerate playlists in '{manager.PlaylistPath}': {exception}");
                return directPlaylists;
            }

            foreach (var playlist in directPlaylists)
            {
                try
                {
                    var matchingFiles = directFiles
                        .Where(path => string.Equals(
                            Path.GetFileNameWithoutExtension(path),
                            playlist.Filename,
                            StringComparison.OrdinalIgnoreCase))
                        .Where(path => manager.GetHandlerForExtension(Path.GetExtension(path)) != null)
                        .ToArray();

                    var suggestedExtension = playlist.SuggestedExtension?.TrimStart('.');
                    var playlistPath = matchingFiles.FirstOrDefault(path => string.Equals(
                            Path.GetExtension(path).TrimStart('.'),
                            suggestedExtension,
                            StringComparison.OrdinalIgnoreCase))
                        ?? matchingFiles.FirstOrDefault();
                    if (playlistPath == null)
                    {
                        continue;
                    }

                    var handler = manager.GetHandlerForExtension(Path.GetExtension(playlistPath));
                    if (handler == null)
                    {
                        continue;
                    }

                    if (!handler.HandledType.IsInstanceOfType(playlist))
                    {
                        Plugin.Log.Warn($"Playlist '{playlist.Filename}' changed format on disk and will be reloaded after restarting the game.");
                        continue;
                    }

                    using var playlistStream = BeatSaberPlaylistsLib.Utilities.OpenFileRead(playlistPath);
                    playlist.Clear();
                    handler.Populate(playlistStream, playlist);
                }
                catch (Exception exception)
                {
                    Plugin.Log.Error($"Could not refresh playlist '{playlist.Filename}' in '{manager.PlaylistPath}': {exception}");
                }
            }

            return directPlaylists;
        }

        public static BeatmapLevelPack[] TryGetAllPlaylistsAsLevelPacks()
        {
            IPlaylist[] playlists = TryGetAllPlaylists();
            BeatmapLevelPack[] levelPacks = new BeatmapLevelPack[playlists.Length];
            for (int i = 0; i < playlists.Length; ++i)
            {
                levelPacks[i] = playlists[i].PlaylistLevelPack;
            }
            return levelPacks;
        }

        #region Image


        private static Stream GetFolderImageStream() =>
            Assembly.GetExecutingAssembly().GetManifestResourceStream("PlaylistManager.Icons.FolderIcon.png");

        internal static async Task<Sprite> GeneratePlaylistIcon(IPlaylist playlist)
        {
            using var coverStream = await playlist.GetDefaultCoverStream();
            if (coverStream != null)
            {
                Sprite? sprite = null;
                await IPA.Utilities.Async.UnityMainThreadTaskScheduler.Factory.StartNew(async () => sprite = await BeatSaberMarkupLanguage.Utilities.LoadSpriteAsync(coverStream.ToArray()));
                return sprite ? sprite : BeatSaberPlaylistsLib.Utilities.DefaultSprite;
            }
            return BeatSaberPlaylistsLib.Utilities.DefaultSprite;
        }

        #endregion
    }
}
