using BeatSaberMarkupLanguage.MenuButtons;
using PlaylistManager.Configuration;
using PlaylistManager.Utilities;
using SongCore;
using SongCore.UI;
using System;
using Zenject;

namespace PlaylistManager.UI
{
    public class RefreshButtonUI : IInitializable, IDisposable
    {
        private readonly Loader _loader;
        private readonly ProgressBar _progressBar;
        private readonly MenuButtons _menuButtons;
        private readonly FoldersViewController _foldersViewController;

        private MenuButton refreshButton;

        private RefreshButtonUI(Loader loader, ProgressBar progressBar, MenuButtons menuButtons, [InjectOptional] FoldersViewController foldersViewController)
        {
            _loader = loader;
            _progressBar = progressBar;
            _menuButtons = menuButtons;
            _foldersViewController = foldersViewController;
        }

        public void Initialize()
        {
            refreshButton = new MenuButton("Refresh Playlists", "Refresh Songs & Playlists", RefreshButtonPressed);
            _menuButtons.RegisterButton(refreshButton);
            Loader.SongsLoadedEvent += SongsLoaded;
        }

        private void SongsLoaded(Loader _, System.Collections.Concurrent.ConcurrentDictionary<string, BeatmapLevel> songs)
        {
            if (!PluginConfig.Instance.FoldersDisabled && _foldersViewController != null)
            {
                var playlistCount = _foldersViewController.RefreshCurrentDirectoryFromDisk();
                _progressBar.AppendText($"\n{playlistCount} playlists loaded here");
            }
            else
            {
                PlaylistLibUtils.playlistManager.RefreshPlaylists(true);
                _progressBar.AppendText($"\n{PlaylistLibUtils.playlistManager.GetPlaylistCount(true)} playlists loaded");
            }
        }

        public void Dispose()
        {
            _menuButtons.UnregisterButton(refreshButton);
            Loader.SongsLoadedEvent -= SongsLoaded;
        }

        private void RefreshButtonPressed()
        {
            if (!Loader.AreSongsLoading)
            {
                _loader.RefreshSongs(fullRefresh: false);
            }
        }
    }
}
