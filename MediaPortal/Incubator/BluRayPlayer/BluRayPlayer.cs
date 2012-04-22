﻿#region Copyright (C) 2007-2011 Team MediaPortal

/*
    Copyright (C) 2007-2011 Team MediaPortal
    http://www.team-mediaportal.com

    This file is part of MediaPortal 2

    MediaPortal 2 is free software: you can redistribute it and/or modify
    it under the terms of the GNU General Public License as published by
    the Free Software Foundation, either version 3 of the License, or
    (at your option) any later version.

    MediaPortal 2 is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with MediaPortal 2. If not, see <http://www.gnu.org/licenses/>.
*/

#endregion

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using DirectShowLib;
using MediaPortal.Common;
using MediaPortal.Common.Localization;
using MediaPortal.Common.Settings;
using MediaPortal.UI.Control.InputManager;
using MediaPortal.UI.Players.Video.Settings;
using MediaPortal.UI.Players.Video.Subtitles;
using MediaPortal.UI.Players.Video.Tools;
using MediaPortal.UI.Presentation.Players;
using MediaPortal.UI.SkinEngine.SkinManagement;
using SlimDX.Direct3D9;

namespace MediaPortal.UI.Players.Video
{
  /// <summary>
  /// BluRayPlayer implements a BluRay player including menu support.
  /// </summary>
  public class BluRayPlayer : VideoPlayer, IDVDPlayer, IBDReaderCallback
  {
    public const double MINIMAL_FULL_FEATURE_LENGTH = 3000;
    public const string RES_PLAYBACK_CHAPTER = "[Playback.Chapter]";

    #region Variables

    protected double[] _chapterTimestamps;
    protected string[] _chapterNames;

    protected readonly string[] _emptyStringArray = new string[0];
    protected IBaseFilter _fileSource;
    protected SubtitleRenderer _subtitleRenderer;
    protected IBaseFilter _subtitleFilter;
    protected IBDReader _bdReader;
    protected BluRayOSDRenderer _osdRenderer;
    protected GraphRebuilder _graphRebuilder;

    protected readonly DeviceEx _device = SkinContext.Device;

    protected BluRayAPI.ChangedMediaType _changedChangedMediaType;
    protected BluRayAPI.BluRayStreamFormats _currentVideoFormat;
    protected BluRayAPI.BluRayStreamFormats _currentAudioFormat;

    protected List<BluRayAPI.BDTitleInfo> _titleInfos;
    protected int _currentTitle;
    protected uint _currentChapter;
    protected BluRayAPI.MenuState _menuState;
    protected bool _forceTitle;
    protected double _currentPos;
    protected double _duration;

    #endregion

    #region Constructor

    /// <summary>
    /// Constructs a BluRayPlayer player object.
    /// </summary>
    public BluRayPlayer()
    {
      PlayerTitle = "BluRayPlayer"; // for logging
      _osdRenderer = new BluRayOSDRenderer(OnTextureInvalidated);
    }

    #endregion

    #region VideoPlayer overrides

    protected override void CreateGraphBuilder()
    {
      base.CreateGraphBuilder();
      // configure EVR
      _streamCount = 2; // Allow Video and Subtitle
    }

    /// <summary>
    /// Adds preferred audio/video codecs.
    /// </summary>
    protected override void AddPreferredCodecs()
    {
      base.AddPreferredCodecs();

      BluRayPlayerSettings settings = ServiceRegistration.Get<ISettingsManager>().Load<BluRayPlayerSettings>();
      if (settings == null)
        return;

      //IAMPluginControl is supported in Win7 and later only.
      try
      {
        IAMPluginControl pc = new DirectShowPluginControl() as IAMPluginControl;
        if (pc != null)
        {
          if (settings.VC1Codec != null)
          {
            BluRayPlayerBuilder.LogDebug("Setting preferred VC-1 codec {0}", settings.VC1Codec);
            pc.SetPreferredClsid(CodecHandler.MEDIASUBTYPE_VC1, settings.VC1Codec.GetCLSID());
          }
        }
      }
      catch
      {
      }
    }

    /// <summary>
    /// Adds the file source filter to the graph.
    /// </summary>
    protected override void AddFileSource()
    {
      // Render the file
      _fileSource = (IBaseFilter)new BDReader();

      // Init BD Reader
      _bdReader = (IBDReader)_fileSource;
      _bdReader.SetD3DDevice(_device.ComPointer);
      _bdReader.SetBDReaderCallback(this);

      _graphBuilder.AddFilter(_fileSource, BluRayAPI.BDREADER_FILTER_NAME);

      _subtitleRenderer = new SubtitleRendererV3(OnTextureInvalidated);
      _subtitleFilter = _subtitleRenderer.AddSubtitleFilter(_graphBuilder);
      if (_subtitleFilter != null)
      {
        _subtitleRenderer.RenderSubtitles = true;
        _subtitleRenderer.SetPlayer(this);
      }

      // Render the file
      IFileSourceFilter f = (IFileSourceFilter)_fileSource;
      string strFile = Path.Combine(_resourceAccessor.LocalFileSystemPath, @"BDMV\index.bdmv");
      f.Load(strFile, null);

      // Init GraphRebuilder
      _graphRebuilder = new GraphRebuilder(_graphBuilder, _fileSource, OnAfterGraphRebuild) { PlayerName = PlayerTitle };

      // Get the complete BD title information (including all streams, chapters...)
      _titleInfos = GetTitleInfoCollection(_bdReader);

      ulong duration = 0;
      uint maxIdx = 0;
      foreach (BluRayAPI.BDTitleInfo bdTitleInfo in _titleInfos)
      {
        if (bdTitleInfo.Duration > duration)
        {
          duration = bdTitleInfo.Duration;
          maxIdx = bdTitleInfo.Index;
        }
      }
      // TEST: play the longest title
      _forceTitle = false;
      if (_forceTitle)
      {
        _bdReader.ForceTitleBasedPlayback(true, maxIdx);
        _currentTitle = (int)maxIdx;
      }
      else
      {
        _bdReader.ForceTitleBasedPlayback(false, 0);
      }

      _bdReader.Start();
    }

    protected override void OnBeforeGraphRunning()
    {
      base.OnBeforeGraphRunning();

      // first all automatically rendered pins
      FilterGraphTools.RenderOutputPins(_graphBuilder, _fileSource);

      // MSDN: "During the connection process, the Filter Graph Manager ignores pins on intermediate filters if the pin name begins with a tilde (~)."
      // then connect the skipped "~" output pins
      FilterGraphTools.RenderAllManualConnectPins(_graphBuilder);
    }

    protected override void OnGraphRunning()
    {
      base.OnGraphRunning();
      EnumerateChapters();
    }

    protected override void FreeCodecs()
    {
      // Free subtitle filter
      FilterGraphTools.TryDispose(ref _subtitleRenderer);
      FilterGraphTools.TryRelease(ref _subtitleFilter);

      // Free OSD renderer
      FilterGraphTools.TryDispose(ref _osdRenderer);

      // Free file source
      FilterGraphTools.TryRelease(ref _fileSource);

      // Free base class
      base.FreeCodecs();
    }

    #endregion

    #region IDVDPlayer Member

    // TODO: implement titles support
    public string[] DvdTitles
    {
      get { return _emptyStringArray; }
    }

    public void SetDvdTitle(string title)
    { }

    public string CurrentDvdTitle
    {
      get { return null; }
    }

    /// <summary>
    /// Enumerates available chapters. Needs to be executed after title changes.
    /// </summary>
    protected void EnumerateChapters()
    {
      _chapterNames = null;
      if (_titleInfos == null || _currentTitle >= _titleInfos.Count)
        return;

      List<string> chapters = new List<string>();
      BluRayAPI.BDTitleInfo currentTitle = _titleInfos[_currentTitle];

      _bdReader.GetChapter(ref _currentChapter);

      for (int i = 1; i <= currentTitle.Chapters.Length; ++i)
        chapters.Add(GetChapterName(i));

      _chapterNames = chapters.ToArray();
    }

    /// <summary>
    /// Returns a localized chapter name.
    /// </summary>
    /// <param name="chapterNumber">0 based chapter number.</param>
    /// <returns>Localized chapter name.</returns>
    private static String GetChapterName(int chapterNumber)
    {
      //Idea: we could scrape chapter names and store them in MediaAspects. When they are available, return the full names here.
      return ServiceRegistration.Get<ILocalization>().ToString(RES_PLAYBACK_CHAPTER, chapterNumber);
    }

    public string[] Chapters
    {
      get { return _chapterNames; }
    }

    public void SetChapter(string chapter)
    {
      string[] chapters = Chapters;
      for (int i = 0; i < chapters.Length; i++)
      {
        if (chapter == chapters[i])
        {
          SetDvdChapter((uint)i);
          return;
        }
      }
    }

    private void SetDvdChapter(uint chapterIndex)
    {
      if (_bdReader != null)
        _bdReader.SetChapter(chapterIndex);
      return;
    }

    public bool ChaptersAvailable
    {
      get { return _chapterNames != null; }
    }

    public void NextChapter()
    {
      if (_bdReader == null)
        return;

      _bdReader.GetChapter(ref _currentChapter);
      SetDvdChapter(_currentChapter + 1);
    }

    public void PrevChapter()
    {
      if (_bdReader == null)
        return;

      _bdReader.GetChapter(ref _currentChapter);
      SetDvdChapter(_currentChapter - 1);
    }

    public string CurrentChapter
    {
      get { return _chapterNames != null ? _chapterNames[_currentChapter] : null; }
    }

    public bool IsHandlingUserInput
    {
      get
      {
        return _osdRenderer != null && _osdRenderer.IsOSDPresent;
      }
    }

    public void ShowDvdMenu()
    {
      if (_bdReader == null)
        return;

      //_menuState = BluRayAPI.MenuState.Root;
      _bdReader.Action((int)BluRayAPI.BDKeys.BD_VK_ROOT_MENU);
    }

    public void OnMouseMove(float x, float y)
    {
      if (_bdReader == null || !IsHandlingUserInput)
        return;

      // Scale relative coordinates to video's size
      x *= VideoSize.Width;
      y *= VideoSize.Height;

      _bdReader.MouseMove((int)x, (int)y);
    }

    public void OnMouseClick(float x, float y)
    {
      if (_bdReader == null || !IsHandlingUserInput)
        return;

      BluRayPlayerBuilder.LogDebug("BDPlayer: Mouse select");
      _bdReader.Action((int)BluRayAPI.BDKeys.BD_VK_MOUSE_ACTIVATE);
    }

    public void OnKeyPress(Key key)
    {
      if (_bdReader == null || !IsHandlingUserInput)
        return;

      BluRayAPI.BDKeys translatedKey;
      if (BluRayAPI.KeyMapping.TryGetValue(key, out translatedKey))
      {
        BluRayPlayerBuilder.LogDebug("BDPlayer: Key Press {0} -> {1}", key, translatedKey);
        _bdReader.Action((int)translatedKey);
      }
    }

    #endregion

    #region IBDReaderCallback Member

    public int OnMediaTypeChanged(BluRayAPI.VideoRate videoRate, BluRayAPI.BluRayStreamFormats videoFormat, BluRayAPI.BluRayStreamFormats audioFormat)
    {
      BluRayPlayerBuilder.LogInfo("OnMediaTypeChanged() - {0} ({1} fps), {2}", videoFormat, videoRate, audioFormat);
      bool requireRebuild = false;

      _changedChangedMediaType = BluRayAPI.ChangedMediaType.None;

      if (videoFormat != _currentVideoFormat)
      {
        if (_currentVideoFormat != BluRayAPI.BluRayStreamFormats.Unknown)
          requireRebuild = true;

        _changedChangedMediaType |= BluRayAPI.ChangedMediaType.Video;
        _currentVideoFormat = videoFormat;
      }

      if (audioFormat != _currentAudioFormat)
      {
        if (_currentAudioFormat != BluRayAPI.BluRayStreamFormats.Unknown)
          requireRebuild = true;

        _changedChangedMediaType |= BluRayAPI.ChangedMediaType.Audio;
        _currentAudioFormat = audioFormat;
      }

      // Only rebuild the graph when we had a former media type (not on first run!)
      if (requireRebuild)
      {
        _graphRebuilder.DoAsynchRebuild();
        _bdReader.OnGraphRebuild(_changedChangedMediaType);
      }

      return _changedChangedMediaType == BluRayAPI.ChangedMediaType.None ? 0 : 1;
    }


    /// <summary>
    /// Informs the ITsReader that the graph rebuild was done.
    /// </summary>
    protected void OnAfterGraphRebuild()
    {
      IBDReader tsReader = (IBDReader)_fileSource;
      tsReader.OnGraphRebuild(_changedChangedMediaType);
    }

    public int OnBDevent(BluRayAPI.BluRayEvent bluRayEvent)
    {
      if (bluRayEvent.Event != 0 &&
        bluRayEvent.Event != (int)BluRayAPI.BDEvents.Still &&
        bluRayEvent.Event != (int)BluRayAPI.BDEvents.StillTime)
      {
        HandleBDEvent(bluRayEvent);
      }
      return 0;
    }

    public int OnOSDUpdate(BluRayAPI.OSDTexture osdInfo)
    {
      _osdRenderer.DrawItem(osdInfo);
      return 0;
    }

    public int OnClockChange(long duration, long position)
    {
      _currentPos = position / 10000000.0;
      _duration = duration / 10000000.0;
      return 0;
    }

    /// <summary>
    /// Render BluRay OSD on video surface if available.
    /// </summary>
    protected override void PostProcessTexture(Surface targetSurface)
    {
      _osdRenderer.DrawOverlay(targetSurface);
      _subtitleRenderer.DrawOverlay(targetSurface);
    }

    #endregion

    #region BD Event handling

    protected void HandleBDEvent(BluRayAPI.BluRayEvent bdevent)
    {
      switch ((BluRayAPI.BDEvents)bdevent.Event)
      {
        case BluRayAPI.BDEvents.AudioStream:
          BluRayPlayerBuilder.LogDebug("Audio changed to {0}", bdevent.Param);
          //if (bdevent.Param != 0xff)
          //  CurrentAudioStream = bdevent.Param - 1;
          break;

        case BluRayAPI.BDEvents.PgText:
          BluRayPlayerBuilder.LogDebug("Subtitles available {0}", bdevent.Param);
          break;

        case BluRayAPI.BDEvents.PgTextStream:
          BluRayPlayerBuilder.LogDebug("Subtitle changed to {0}", bdevent.Param);
          //if (bdevent.Param != 0xfff)
          //  CurrentSubtitleStream = bdevent.Param;
          break;

        case BluRayAPI.BDEvents.IgStream:
          BluRayPlayerBuilder.LogDebug("Interactive graphics available {0}", bdevent.Param);
          break;

        case BluRayAPI.BDEvents.Playlist:
          BluRayPlayerBuilder.LogDebug("Playlist changed to {0}", bdevent.Param);
          if (_forceTitle || (_currentTitle != (int)BluRayAPI.BluRayTitle.FirstPlay && _currentTitle != (int)BluRayAPI.BluRayTitle.TopMenu))
            EnumerateChapters();
          break;

        case BluRayAPI.BDEvents.Playitem:
          BluRayPlayerBuilder.LogDebug("Playitem changed to {0}", bdevent.Param);
          EnumerateChapters();
          //if (menuState == BluRayAPI.MenuState.Root && chapters != null && _currentTitle != BLURAY_TITLE_FIRST_PLAY && _currentTitle != BLURAY_TITLE_TOP_MENU)
          //  menuItems = MenuItems.All;
          break;

        case BluRayAPI.BDEvents.Title:
          BluRayPlayerBuilder.LogDebug("Title changed to {0}", bdevent.Param);
          _currentTitle = bdevent.Param;
          _currentChapter = 0xffff;
          switch ((BluRayAPI.BluRayTitle)bdevent.Param)
          {
            case BluRayAPI.BluRayTitle.TopMenu:
            case BluRayAPI.BluRayTitle.FirstPlay:
              if (!_forceTitle)
              {
                //menuItems = MenuItems.None;
                _menuState = BluRayAPI.MenuState.Root;
              }
              break;
            default:
              _menuState = BluRayAPI.MenuState.None;
              break;
          }
          break;

        case BluRayAPI.BDEvents.Chapter:
          BluRayPlayerBuilder.LogDebug("Chapter changed to {0}", bdevent.Param);
          if (bdevent.Param != 0xffff)
            _currentChapter = (uint)bdevent.Param - 1;
          break;

        case BluRayAPI.BDEvents.CustomEventMenuVisibility:
          //if (bdevent.Param == 1)
          //{
          //  BluRayPlayerBuilder.LogDebug("Toggle menu on");
          //if (menuState == BluRayAPI.MenuState.PopUp)
          //  menuItems = MenuItems.All;
          //else
          //  menuItems = MenuItems.None;

          //_iMenuOffPendingCount = 0;
          //_bMenuOn = true;
          //}
          //else if (_iMenuOffPendingCount == 0)
          //{
          //  _iMenuOffPendingCount++;
          //}
          break;
      }
    }

    #endregion

    /// <summary>
    /// Gets the title info collection from the given BDReader object.
    /// </summary>
    /// <param name="reader">IBDReader object</param>
    /// <returns>a collection of titles</returns>
    protected virtual List<BluRayAPI.BDTitleInfo> GetTitleInfoCollection(IBDReader reader)
    {
      uint titleCount = 0;
      reader.GetTitleCount(ref titleCount);

      List<BluRayAPI.BDTitleInfo> titles = new List<BluRayAPI.BDTitleInfo>((int)titleCount);
      BluRayPlayerBuilder.LogDebug("Title count - {0}", titleCount);
      for (int i = 0; i < titleCount; i++)
      {
        BluRayAPI.BDTitleInfo titleInfo = GetTitleInfo(reader, i);
        titles.Add(titleInfo);
      }

      return titles;
    }

    /// <summary>
    /// Gets the title info for the specified index
    /// </summary>
    /// <param name="reader">IBDReader object</param>
    /// <param name="index">index of the title</param>
    /// <returns></returns>
    protected virtual BluRayAPI.BDTitleInfo GetTitleInfo(IBDReader reader, int index)
    {
      BluRayAPI.BDTitleInfo titleInfo = new BluRayAPI.BDTitleInfo();
      IntPtr ptr = IntPtr.Zero;
      try
      {
        ptr = reader.GetTitleInfo(index);
        BluRayAPI.UnmanagedBDTitleInfo umTitleInfo = (BluRayAPI.UnmanagedBDTitleInfo)
          Marshal.PtrToStructure(ptr, typeof(BluRayAPI.UnmanagedBDTitleInfo));

        titleInfo = new BluRayAPI.BDTitleInfo
                      {
                        AngleCount = umTitleInfo.AngleCount,
                        Duration = umTitleInfo.Duration,
                        Index = umTitleInfo.Index,
                        Playlist = umTitleInfo.Playlist,
                        Clips = new BluRayAPI.BDClipInfo[umTitleInfo.ClipCount],
                        Chapters = new BluRayAPI.BDChapter[umTitleInfo.ChapterCount]
                      };

        for (int i = 0; i < umTitleInfo.ClipCount; i++)
        {
          BluRayAPI.UnmanagedBDClipInfo umClipInfo = (BluRayAPI.UnmanagedBDClipInfo)
            Marshal.PtrToStructure(new IntPtr((int)umTitleInfo.Clips + i * Marshal.SizeOf(typeof(BluRayAPI.UnmanagedBDClipInfo))),
            typeof(BluRayAPI.UnmanagedBDClipInfo));
          BluRayAPI.BDClipInfo clipInfo = new BluRayAPI.BDClipInfo
                                                        {
                                                          AudioStreams = new BluRayAPI.BDStreamInfo[umClipInfo.AudioStreamCount],
                                                          IgStreams = new BluRayAPI.BDStreamInfo[umClipInfo.IgStreamCount],
                                                          PgStreams = new BluRayAPI.BDStreamInfo[umClipInfo.PgStreamCount],
                                                          RawStreams = new BluRayAPI.BDStreamInfo[umClipInfo.RawStreamCount],
                                                          VideoStreams = new BluRayAPI.BDStreamInfo[umClipInfo.VideoStreamCount],
                                                          SecAudioStreams = new BluRayAPI.BDStreamInfo[umClipInfo.SecAudioStreamCount],
                                                          SecVideoStreams = new BluRayAPI.BDStreamInfo[umClipInfo.SecVideoStreamCount],
                                                          PktCount = umClipInfo.PktCount,
                                                          StillMode = umClipInfo.StillMode,
                                                          StillTime = umClipInfo.StillTime
                                                        };

          GetStreamInfo(clipInfo.VideoStreams, umClipInfo.VideoStreams);
          GetStreamInfo(clipInfo.AudioStreams, umClipInfo.AudioStreams);
          GetStreamInfo(clipInfo.IgStreams, umClipInfo.IgStreams);
          GetStreamInfo(clipInfo.PgStreams, umClipInfo.PgStreams);
          GetStreamInfo(clipInfo.RawStreams, umClipInfo.RawStreams);
          GetStreamInfo(clipInfo.SecAudioStreams, umClipInfo.SecAudioStreams);
          GetStreamInfo(clipInfo.SecVideoStreams, umClipInfo.SecVideoStreams);
          titleInfo.Clips[i] = clipInfo;
        }

        for (int i = 0; i < titleInfo.Chapters.Length; i++)
        {
          BluRayAPI.BDChapter chapter = (BluRayAPI.BDChapter)
            Marshal.PtrToStructure(new IntPtr((int)umTitleInfo.Chapters + i * Marshal.SizeOf(typeof(BluRayAPI.BDChapter))),
            typeof(BluRayAPI.BDChapter));
          titleInfo.Chapters[i] = chapter;
        }
      }
      catch
      {
        BluRayPlayerBuilder.LogError("GetTitleInfo({0}) failed.", index);
      }
      finally
      {
        if (ptr != IntPtr.Zero)
          reader.FreeTitleInfo(ptr);
      }

      return titleInfo;
    }

    private static void GetStreamInfo(BluRayAPI.BDStreamInfo[] streamInfos, IntPtr ptrStreamInfo)
    {
      for (int i = 0; i < streamInfos.Length; i++)
      {
        BluRayAPI.BDStreamInfo streamInfo = (BluRayAPI.BDStreamInfo)
          Marshal.PtrToStructure(new IntPtr((int)ptrStreamInfo + i * Marshal.SizeOf(typeof(BluRayAPI.BDStreamInfo))),
            typeof(BluRayAPI.BDStreamInfo));

        streamInfos[i] = streamInfo;
      }
    }

    public override TimeSpan CurrentTime
    {
      get
      {
        return ToTimeSpan(_currentPos);
      }
      set
      {
        base.CurrentTime = value;
      }
    }

    public override TimeSpan Duration
    {
      get
      {
        return ToTimeSpan(_duration);
      }
    }

    /// <summary>
    /// Converts a <see cref="double"/> time stamp to <see cref="TimeSpan"/>.
    /// </summary>
    /// <param name="timeStamp">Time stamp to convert.</param>
    /// <returns>TimeSpan instance.</returns>
    private static TimeSpan ToTimeSpan(double timeStamp)
    {
      return new TimeSpan(0, 0, 0, 0, (int)(timeStamp * 1000.0f));
    }
  }
}
