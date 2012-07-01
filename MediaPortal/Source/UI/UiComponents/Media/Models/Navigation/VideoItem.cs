#region Copyright (C) 2007-2012 Team MediaPortal

/*
    Copyright (C) 2007-2012 Team MediaPortal
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
using MediaPortal.Common.Localization;
using MediaPortal.Common.MediaManagement;
using MediaPortal.Common.MediaManagement.DefaultItemAspects;
using MediaPortal.UiComponents.Media.General;

namespace MediaPortal.UiComponents.Media.Models.Navigation
{
  public class VideoItem : PlayableMediaItem
  {
    public VideoItem(MediaItem mediaItem) : base(mediaItem)
    {
      MediaItemAspect videoAspect;
      if (mediaItem.Aspects.TryGetValue(VideoAspect.ASPECT_ID, out videoAspect))
      {
        long? duration = (long?) videoAspect[VideoAspect.ATTR_DURATION];
        SimpleTitle = Title;
        Duration = duration.HasValue ? FormattingUtils.FormatMediaDuration(TimeSpan.FromSeconds((int) duration.Value)) : string.Empty;
        AudioEncoding = (string) videoAspect[VideoAspect.ATTR_AUDIOENCODING];
        VideoEncoding = (string) videoAspect[VideoAspect.ATTR_VIDEOENCODING];
        StoryPlot = (string) videoAspect[VideoAspect.ATTR_STORYPLOT];
      }
    }

    public string Duration
    {
      get { return this[Consts.KEY_DURATION]; }
      set { SetLabel(Consts.KEY_DURATION, value); }
    }

    public string AudioEncoding
    {
      get { return this[Consts.KEY_AUDIO_ENCODING]; }
      set { SetLabel(Consts.KEY_AUDIO_ENCODING, value); }
    }

    public string VideoEncoding
    {
      get { return this[Consts.KEY_VIDEO_ENCODING]; }
      set { SetLabel(Consts.KEY_VIDEO_ENCODING, value); }
    }

    public string StoryPlot
    {
      get { return this[Consts.KEY_STORY_PLOT]; }
      set { SetLabel(Consts.KEY_STORY_PLOT, value); }
    }
  }
}