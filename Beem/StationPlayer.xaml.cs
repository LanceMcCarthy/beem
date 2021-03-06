﻿using Beem.Azure;
using Beem.Core;
using Beem.Core.Models;
using Beem.Settings;
using Beem.Utility;
using Coding4Fun.Toolkit.Controls;
using Coding4Fun.Toolkit.Storage;
using Microsoft.Devices;
using Microsoft.Phone.BackgroundAudio;
using Microsoft.Phone.Controls;
using Microsoft.Phone.Tasks;
using Microsoft.Xna.Framework.GamerServices;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.IO.IsolatedStorage;
using System.Linq;
using System.Net;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Resources;

namespace Beem
{
    public partial class StationPlayer : PhoneApplicationPage
    {
        // The name of the current track that is being recorded.
        string TrackName = string.Empty;

        public StationPlayer()
        {
            InitializeComponent();
            BackgroundAudioPlayer.Instance.PlayStateChanged += new EventHandler(Instance_PlayStateChanged);
        }

        protected async override void OnNavigatedTo(System.Windows.Navigation.NavigationEventArgs e)
        {
            try
            {
                // Navigated from a tile, time to get the stations.

                if (NavigationContext.QueryString.ContainsKey("station"))
                {
                    string station = NavigationContext.QueryString["station"];

                    // no skipping yet - no stations
                    btnNext.IsEnabled = btnPrevious.IsEnabled = false;

                    SettingsManager.AttemptToLoadSettings();

                    Utility.StationManager.DeserializeFavorites();

                    List<Station> stations = (await MobileServiceClientHelper.GetAllStations()).ToList();
                    if (stations != null)
                    {
                        Serialize.Save("stationcache.xml", stations);

                        // now it's OK to navigate through stations.
                        btnNext.IsEnabled = btnPrevious.IsEnabled = true;

                        // after all, I need this because the user might do
                        // "station hopping"
                        Binder.Instance.Stations = new System.Collections.ObjectModel.ObservableCollection<Station>(stations);

                        Station currentStation = (from c in
                                                      Binder.Instance.Stations
                                                  where c.JSONID == station
                                                  select c).Single();

                        Binder.Instance.CurrentStation = currentStation;

                        imgFave.Source = FavoriteImageSetter.GetImage(Binder.Instance.CurrentStation);

                        CheckCurrentPlayingState();

                        SetMediaItem(); // This is used for Zune Hub integration.
                    }
                }
                else
                {
                    CheckCurrentPlayingState();
                    SetMediaItem();
                }
            }
            catch
            {

            }

            StartStoryboard();

            base.OnNavigatedTo(e);
        }

        private void StartStoryboard()
        {
            Storyboard board = this.Resources["ScrollBoxAnimation"] as Storyboard;
            board.Begin();
        }

        private void SetMediaItem()
        {
            MediaHistoryItem item = new MediaHistoryItem();

            StreamResourceInfo localImage = Application.GetResourceStream(new Uri("Images/beem_media.jpg", UriKind.Relative));
            item.ImageStream = localImage.Stream;
            item.Source = "";
            item.Title = Binder.Instance.CurrentStation.Name;
            item.PlayerContext.Add("DI.FM", Binder.Instance.CurrentStation.Name);
            MediaHistory.Instance.NowPlaying = item;

            localImage = Application.GetResourceStream(new Uri("Images/beem_media_micro.jpg", UriKind.Relative));
            item.ImageStream = localImage.Stream;
            MediaHistory.Instance.WriteRecentPlay(item);
        }

        private async void CheckCurrentPlayingState()
        {
            try
            {
                // Download the track list first, so that I can manipulate the data
                // to be passed to the player.
                TrackListDownloader downloader = new TrackListDownloader();
                var trackList = await downloader.GetTracksForStation(Binder.Instance.CurrentStation.JSONID);
                Binder.Instance.CurrentStation.NowPlaying = trackList.First();
                trackList.Remove(trackList.First());

                Binder.Instance.CurrentStation.TrackList = new ObservableCollection<Track>(trackList);

                // The first track in the collection tells what's playing now. It does not
                // need to be in the track history

                // Also make sure that we scrobble the track to Last.fm if necessary.
                if (Binder.Instance.CurrentAppSettings.ScrobbleOnLaunch)
                {
                    ScrobbleCurrentTrack(false);
                }

                if (BackgroundAudioPlayer.Instance.Track != null)
                {
                    if (Binder.Instance.CurrentStation != null &&
                        BackgroundAudioPlayer.Instance.Track.Title != Binder.Instance.CurrentStation.NowPlaying.FullTrackName)
                    {
                        SetAudioTrack();
                    }
                    else
                    {
                        SetAppropriateImage(false);
                    }
                }
                else
                {
                    SetAudioTrack();
                }
            }
            catch
            {
                MessageBox.Show("A problem happened, we are really sorry! Start by checking your Internet connection.",
                    "Beem", MessageBoxButton.OK);
            }
        }

        private void SetAudioTrack()
        {
            try
            {
                Uri stationLocation;

                if (!Binder.Instance.DISettings.IsPremiumEnabled)
                {
                    stationLocation = new Uri(Binder.Instance.CurrentStation.Location);
                }
                else
                {
                    stationLocation = new Uri(Binder.Instance.CurrentStation.PremiumLocation + "?" + Binder.Instance.DISettings.PremiumKey);
                }

                AudioTrack track = new AudioTrack(stationLocation,
                    Binder.Instance.CurrentStation.NowPlaying.FullTrackName,
                    Binder.Instance.CurrentStation.Name,
                    string.Empty,
                    new Uri(Binder.Instance.CurrentStation.Image), null, EnabledPlayerControls.All);


                BackgroundAudioPlayer.Instance.Track = track;

                BackgroundAudioPlayer.Instance.Play();
            }
            catch
            {
                Debug.WriteLine("Could not play audio track.");
            }
        }

        private void btnPlay_Click(object sender, RoutedEventArgs e)
        {
            SetAppropriateImage(true);
        }

        private void SetAppropriateImage(bool performAction)
        {
            if (BackgroundAudioPlayer.Instance.PlayerState == PlayState.Playing)
            {
                if (performAction)
                {
                    BackgroundAudioPlayer.Instance.Pause();
                    btnPlay.ImageSource = new BitmapImage(new Uri("/Images/appbar.control.play.png", UriKind.Relative));
                }
                else
                    btnPlay.ImageSource = new BitmapImage(new Uri("/Images/appbar.control.pause.png", UriKind.Relative));
            }
            else
            {
                if (performAction)
                {
                    BackgroundAudioPlayer.Instance.Play();
                    btnPlay.ImageSource = new BitmapImage(new Uri("/Images/appbar.control.pause.png", UriKind.Relative));
                }
                else
                    btnPlay.ImageSource = new BitmapImage(new Uri("/Images/appbar.control.play.png", UriKind.Relative));
            }
        }

        async void Instance_PlayStateChanged(object sender, EventArgs e)
        {
            try
            {
                Station station = StationNavigator.GetStationByName(BackgroundAudioPlayer.Instance.Track.Artist,
                    Binder.Instance.Stations);

                if (Binder.Instance.CurrentStation != station)
                {
                    // Since the station has switched, it makes sense to stop 
                    // the recording and prompt the user to enter a name for the
                    // recorded track.
                    if (Binder.Instance.IsRecording)
                        StopRecording();

                    Binder.Instance.CurrentStation = station;
                    TrackListDownloader downloader = new TrackListDownloader();

                    var result = await downloader.GetTracksForStation(station.JSONID);
                    Binder.Instance.CurrentStation.NowPlaying = result.First();
                    result.Remove(result.First());
                    Binder.Instance.CurrentStation.TrackList = new ObservableCollection<Track>(result);
                }

                if (BackgroundAudioPlayer.Instance.PlayerState == PlayState.TrackReady)
                    BackgroundAudioPlayer.Instance.Play();

                SetAppropriateImage(false);
            }
            catch
            {
                Debug.WriteLine("Apparently no internet and track cannot be assigned.");
            }
        }

        private void imgFave_Loaded(object sender, RoutedEventArgs e)
        {
            ((Image)sender).Source = Utility.FavoriteImageSetter.GetImage(Binder.Instance.CurrentStation);
        }

        private void btnPrevious_Click(object sender, RoutedEventArgs e)
        {
            if (!Binder.Instance.IsRecording)
            {
                NavigateToPreviousStation();
            }
            else
            {
                ConfirmBeforeSwitch(false);
            }
        }

        private void btnNext_Click(object sender, RoutedEventArgs e)
        {
            if (!Binder.Instance.IsRecording)
            {
                NavigateToNextStation();
            }
            else
            {
                ConfirmBeforeSwitch(true);
            }
        }

        private void ConfirmBeforeSwitch(bool isNext)
        {
            if (MessageBox.Show("You have a recording in progress. Do you want to stop it before changing stations?", "Beem", MessageBoxButton.OKCancel) == MessageBoxResult.OK)
            {
                StopRecording();

                if (isNext)
                {
                    NavigateToNextStation();
                }
                else
                {
                    NavigateToPreviousStation();
                }
            }
        }

        private void NavigateToPreviousStation()
        {
            try
            {
                Station station = StationNavigator.GetPreviousStation(Binder.Instance.CurrentStation.Name,
                    Binder.Instance.Stations.ToList());

                if (station == null)
                {
                    MessageBox.Show("There are no stations before this one, so we can't do the switch.", "Beem", MessageBoxButton.OK);
                }
                else
                {
                    Binder.Instance.CurrentStation = station;
                    imgFave.Source = Utility.FavoriteImageSetter.GetImage(Binder.Instance.CurrentStation);
                    CheckCurrentPlayingState();
                }
            }
            catch
            {
                MessageBox.Show("Can't navigate to that station. It looks like that station is missing. Make sure you are connected to a network and try again.",
                    "Beem", MessageBoxButton.OK);
            }
        }

        private void NavigateToNextStation()
        {
            try
            {
                Station station = StationNavigator.GetNextStation(Binder.Instance.CurrentStation.Name,
                    Binder.Instance.Stations.ToList());

                if (station == null)
                {
                    MessageBox.Show("There are no stations after this one, so we can't do the switch.", "Beem", MessageBoxButton.OK);
                }
                else
                {
                    Binder.Instance.CurrentStation = station;
                    imgFave.Source = Utility.FavoriteImageSetter.GetImage(Binder.Instance.CurrentStation);
                    CheckCurrentPlayingState();
                }
            }
            catch
            {
                MessageBox.Show("Can't navigate to that station. It looks like that station is missing. Make sure you are connected to a network and try again.",
                    "Beem", MessageBoxButton.OK);
            }
        }

        private void btnRecord_Click(object sender, RoutedEventArgs e)
        {
            StartRecording();
        }

        private void StartRecording()
        {
            if (Binder.Instance.CurrentStation.NowPlaying != null)
            {
                if (Binder.Instance.CurrentAppSettings.ShowRecordingAlert)
                {
                    CheckBox box = new CheckBox();
                    box.Content = "Don't show this again.";

                    CustomMessageBox messageBox = new CustomMessageBox
                    {
                        Caption = "Beem",
                        Message = "You need to keep the application in the foreground for the recording to happen. Start recording?\n\nNOTE: If you are using DI.FM Premium, the standard (96kbps) stream is recorded. Currently, we do not allow hi-def stream recording.",
                        LeftButtonContent = "yes",
                        RightButtonContent = "no",
                        Content = box
                    };

                    messageBox.Dismissed += (s, e) =>
                        {
                            if (e.Result == CustomMessageBoxResult.LeftButton)
                            {
                                if (box.IsChecked == true)
                                {
                                    Binder.Instance.CurrentAppSettings.ShowRecordingAlert = false;
                                    SettingsManager.StoreSettings();
                                }

                                InitiateRecordingProcess();
                            }
                        };

                    messageBox.Show();
                }
                else
                {
                    InitiateRecordingProcess();
                }
            }
        }

        void InitiateRecordingProcess()
        {
            TrackName = Binder.Instance.CurrentStation.NowPlaying.FullTrackName.Replace(" ", "_");
            Binder.Instance.RecordingContents = new MemoryStream();

            Binder.Instance.IsRecording = true;

            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(Binder.Instance.CurrentStation.Location);
            request.AllowReadStreamBuffering = false;
            request.Method = "GET";
            request.BeginGetResponse(new AsyncCallback(GetShoutAsync), request);
        }

        void GetShoutAsync(IAsyncResult res)
        {
            try
            {
                HttpWebRequest request = (HttpWebRequest)res.AsyncState;
                HttpWebResponse response = (HttpWebResponse)request.EndGetResponse(res);

                Stream r = response.GetResponseStream();

                byte[] data = new byte[4096];
                int read;

                while ((read = r.Read(data, 0, data.Length)) > 0 && Binder.Instance.IsRecording)
                {
                    Binder.Instance.RecordingContents.Write(data, 0, read);
                    Binder.Instance.RecordingLength = Convert.ToInt32(Binder.Instance.RecordingContents.Length / 1024);
                }
            }
            catch
            {
                Dispatcher.BeginInvoke(() =>
                    {
                        MessageBox.Show("There was a problem recording this stream. Please check your Internet connection. If you are recording a DI.FM Premium stream, make sure that your key is valid",
                            "Beem", MessageBoxButton.OK);
                    });
                Binder.Instance.IsRecording = false;
            }
        }

        private void btnRecordStop_Click(object sender, RoutedEventArgs e)
        {
            StopRecording();
        }

        private void StopRecording()
        {
            Binder.Instance.IsRecording = false;

            DisplaySavePrompt();
        }

        void DisplaySavePrompt()
        {
            Deployment.Current.Dispatcher.BeginInvoke(() =>
            {
                InputPrompt prompt = new InputPrompt();
                prompt.Title = "Beem";
                prompt.Message = "Enter a file name to use for the recording. MP3 extension added automatically.";
                prompt.Value = TrackName;
                prompt.IsCancelVisible = true;
                prompt.Completed += new EventHandler<PopUpEventArgs<string, PopUpResult>>(prompt_Completed);
                prompt.Show();
            });
        }

        void prompt_Completed(object sender, PopUpEventArgs<string, PopUpResult> e)
        {
            if (e.PopUpResult == PopUpResult.Ok)
            {
                if (!string.IsNullOrEmpty(e.Result))
                {
                    string testFileName = e.Result + ".mp3";

                    if (IsValidFilename(testFileName))
                    {
                        try
                        {
                            using (IsolatedStorageFile file = IsolatedStorageFile.GetUserStoreForApplication())
                            {
                                if (!file.DirectoryExists("Music"))
                                    file.CreateDirectory("Music");

                                if (!file.FileExists("/Music/" + testFileName))
                                {
                                    WriteFile(testFileName, file);
                                }
                                else
                                {
                                    if (MessageBox.Show("File already exists. Overwrite?", "Beem", MessageBoxButton.OKCancel) == MessageBoxResult.OK)
                                    {
                                        WriteFile(testFileName, file);
                                    }
                                    else
                                    {
                                        DisplaySavePrompt();
                                    }
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine(ex.Message);
                            MessageBox.Show("For some reason, Beem cannot save this file. Check the amount of available space.", "Beem", MessageBoxButton.OK);
                        }
                    }
                    else
                    {
                        Deployment.Current.Dispatcher.BeginInvoke(() =>
                        {
                            MessageBox.Show("File name contains invalid characters.", "Beem", MessageBoxButton.OK);
                        });
                    }
                }
                else
                {
                    Deployment.Current.Dispatcher.BeginInvoke(() =>
                        {
                            MessageBox.Show("You nee to specify a file name to save this recording.", "Beem", MessageBoxButton.OK);
                        });
                }
            }

            Binder.Instance.RecordingLength = 0;
        }


        private void WriteFile(string fileName, IsolatedStorageFile file)
        {
            using (var writer = new IsolatedStorageFileStream("/Music/" + fileName, FileMode.Create, file))
            {
                Binder.Instance.RecordingContents.Position = 0;
                byte[] data = Binder.Instance.RecordingContents.ToArray();
                writer.Write(data, 0, data.Length);
            }
        }

        bool IsValidFilename(string name)
        {
            char[] invalid = Path.GetInvalidPathChars();

            foreach (char c in invalid)
            {
                if (name.Contains(c.ToString()))
                    return false;
            }

            return true;
        }

        private void TextBlock_Tap(object sender, System.Windows.Input.GestureEventArgs e)
        {
            Track track = (Track)((Grid)sender).Tag;
            SearchMarketplace(string.Format("{0} {1}", track.Artist, track.Title));
        }

        private void SearchMarketplace(string term)
        {
            MarketplaceSearchTask task = new MarketplaceSearchTask();
            task.ContentType = MarketplaceContentType.Music;

            task.SearchTerms = term;
            try
            {
                task.Show();
            }
            catch
            {

            }
        }

        protected override void OnBackKeyPress(System.ComponentModel.CancelEventArgs e)
        {
            if (Binder.Instance.IsRecording)
            {
                if (MessageBox.Show("You have a recording in progress. Do you want to stop it before leaving this?", "Beem", MessageBoxButton.OKCancel) == MessageBoxResult.OK)
                {
                    StopRecording();

                    base.OnBackKeyPress(e);
                }
                else
                {
                    e.Cancel = true;
                }
            }
            else
            {
                base.OnBackKeyPress(e);
            }
        }

        void ScrobbleCurrentTrack(bool showNotification = true)
        {
            if (Binder.Instance.CurrentAppSettings.Session != null &&
                !string.IsNullOrEmpty(Binder.Instance.CurrentAppSettings.Session.Key))
            {
                Dispatcher.BeginInvoke(() =>
                    {
                        try
                        {
                            string trackArtist = string.IsNullOrEmpty(Binder.Instance.CurrentStation.NowPlaying.Artist) ?
                                Binder.Instance.CurrentStation.Name : Binder.Instance.CurrentStation.NowPlaying.Artist;

                            App.LFMClient.ScrobbleTrack(
                            trackArtist,
                            Binder.Instance.CurrentStation.NowPlaying.Title,
                            Binder.Instance.CurrentAppSettings.Session.Key,
                            (data) =>
                            {
                                if (showNotification)
                                {
                                    MessageBox.Show("Scrobbled track to Last.fm!", "Beem", MessageBoxButton.OK);
                                }
                            });
                        }
                        catch
                        {
                            MessageBox.Show("Can't scrobble track. Make sure you are logged into Last.fm and you are connected to a network.",
                                "Beem", MessageBoxButton.OK);
                        }
                    });
            }
            else
            {
                if (showNotification)
                {
                    Dispatcher.BeginInvoke(() =>
                        {
                            MessageBox.Show("Cannot scrobble track. Try authenticating with Last.fm first.", "Beem",
                                MessageBoxButton.OK);
                        });
                }
            }
        }

        private void btnMarket_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                SearchMarketplace(string.Format("{0} {1}", Binder.Instance.CurrentStation.NowPlaying.Artist,
                    Binder.Instance.CurrentStation.NowPlaying.Title));
            }
            catch
            {
                MessageBox.Show("Oops! Looks like we can't reach the Marketplace right now. Try again later.", "Beem", MessageBoxButton.OK);
            }
        }

        private void btnPin_Click(object sender, RoutedEventArgs e)
        {
            Utility.StationManager.Pin(Binder.Instance.CurrentStation);
        }

        private void btnShare_Click(object sender, RoutedEventArgs e)
        {
            // The station is null if there was a problem with the Internet connection.
            if (Binder.Instance.CurrentStation.NowPlaying != null)
            {
                Guide.BeginShowMessageBox("Beem", "Do you want to share the track through the Windows Phone social channels or Last.fm?",
                    new List<string> { "windows phone", "last.fm" }, 0, MessageBoxIcon.None, (res) =>
                        {
                            int? result = Guide.EndShowMessageBox(res);
                            if (result == 0)
                            {
                                ShareLinkTask shareLink = new ShareLinkTask();
                                shareLink.LinkUri = new Uri("http://bitly.com/BeemPlus");
                                shareLink.Message = "Listening to " + Binder.Instance.CurrentStation.NowPlaying.FullTrackName + " with #BeemWP.";
                                shareLink.Show();
                            }
                            else if (result == 1)
                            {
                                ScrobbleCurrentTrack();
                            }
                        }, null);
            }
            else
            {
                MessageBox.Show("Apparently there is no Internet connection, so we can't share your track at this time.", "Beem", MessageBoxButton.OK);
            }
        }

        private void btnFavorite_Click(object sender, RoutedEventArgs e)
        {
            if (Utility.StationManager.CheckIfExists(Binder.Instance.CurrentStation))
            {
                Utility.StationManager.Remove(Binder.Instance.CurrentStation);
            }
            else
            {
                Binder.Instance.FavoriteStations.Add(Binder.Instance.CurrentStation);
            }

            Utility.StationManager.SerializeFavorites();
            imgFave.Source = Utility.FavoriteImageSetter.GetImage(Binder.Instance.CurrentStation);
        }

        private void btnCopy_Click(object sender, RoutedEventArgs e)
        {
            if (Binder.Instance.CurrentStation != null && Binder.Instance.CurrentStation.NowPlaying != null)
            {
                Clipboard.SetText(Binder.Instance.CurrentStation.NowPlaying.FullTrackName);
                MessageBox.Show("Track name copied to clipboard!", "Beem", MessageBoxButton.OK);
            }
            else
            {
                MessageBox.Show("Nothing to copy yet!", "Beem", MessageBoxButton.OK);
            }
        }
    }
}