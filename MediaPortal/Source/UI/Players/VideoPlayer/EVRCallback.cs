#region Copyright (C) 2007-2011 Team MediaPortal

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

/****************************************************************************
While the underlying libraries are covered by LGPL, this sample is released 
as public domain.  It is distributed in the hope that it will be useful, but 
WITHOUT ANY WARRANTY; without even the implied warranty of MERCHANTABILITY 
or FITNESS FOR A PARTICULAR PURPOSE.  
*****************************************************************************/

using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;
using MediaPortal.UI.Players.Video.Tools;
using MediaPortal.UI.SkinEngine.SkinManagement;
using SlimDX.Direct3D9;

namespace MediaPortal.UI.Players.Video
{
  public delegate void VideoSizePresentDlgt(EVRCallback sender);

  [ComVisible(true), ComImport,
   Guid("324FAA1F-7DA6-4778-833B-3993D8FF4151"),
   InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
  public interface IEVRPresentCallback
  {
    /// <summary>
    /// Callback from EVRPresenter.dll to display a DirectX texture.
    /// </summary>
    /// <param name="cx">Video width.</param>
    /// <param name="cy">Video height.</param>
    /// <param name="arx">Aspect Ratio X.</param>
    /// <param name="ary">Aspect Ratio Y.</param>
    /// <param name="dwTexture">Address of the DirectX surface.</param>
    /// <returns><c>0</c>, if the method succeeded, <c>!= 0</c> else.</returns>
    [PreserveSig]
    int PresentTexture(Int16 cx, Int16 cy, Int16 arx, Int16 ary, uint dwTexture);
  }

  public delegate void RenderDlgt();

  [ComVisible(true)]
  [ClassInterface(ClassInterfaceType.None)]
  public class EVRCallback : IEVRPresentCallback, IDisposable
  {
    #region Variables

    private const int MAX_RENDER_TIME = 10; // the maximum time in ms we will wait for render of scene to finish
    private readonly object _lock = new object();
    private Size _originalVideoSize = Size.Empty;
    private Size _aspectRatio = Size.Empty;
    private Texture _texture = null;

    private readonly RenderDlgt _renderDlgt;
    private readonly Action _onTextureInvalidated;
    private readonly ManualResetEvent _renderFinished = new ManualResetEvent(false);

    #endregion

    public EVRCallback(RenderDlgt renderDlgt, Action onTextureInvalidated)
    {
      _onTextureInvalidated = onTextureInvalidated;
      _renderDlgt = renderDlgt;
      SkinContext.DeviceSceneEnd += RenderComplete;
    }

    public void Dispose()
    {
      VideoSizePresent = null;
      FreeSurface();
    }

    #region Public properties and events

    /// <summary>
    /// The first time the <see cref="OriginalVideoSize"/> property is present is when the EVR presenter delivered the first video frame. 
    /// At that time, this event will be raised.
    /// </summary>
    public event VideoSizePresentDlgt VideoSizePresent;

    public Texture Texture
    {
      get { return _texture; }
    }

    public object TextureLock
    {
      get { return _lock; }
    }

    /// <summary>
    /// Gets the size of the original video frame as it came from the EVR presenter.
    /// </summary>
    public Size OriginalVideoSize
    {
      get { return _originalVideoSize; }
    }

    /// <summary>
    /// Gets the aspect ratio.
    /// </summary>
    public Size AspectRatio
    {
      get { return _aspectRatio; }
    }

    #endregion

    private void FreeSurface()
    {
      lock (_lock)
        FilterGraphTools.TryDispose(ref _texture);
    }

    #region IEVRPresentCallback implementation

    public int PresentTexture(short cx, short cy, short arx, short ary, uint dwTexture)
    {
      _renderFinished.Reset();

      lock (_lock)
      {
        FreeSurface();
        if (dwTexture != 0 && cx != 0 && cy != 0)
        {
          _texture = Texture.FromPointer(new IntPtr(dwTexture));
          _originalVideoSize = new Size(cx, cy);
          _aspectRatio = new Size(arx, ary);
        }
      }
      VideoSizePresentDlgt vsp = VideoSizePresent;
      if (vsp != null)
      {
        vsp(this);
        VideoSizePresent = null;
      }

      // Inform caller that we have changed the texture
      if (_onTextureInvalidated != null)
        _onTextureInvalidated();

      if (_renderDlgt != null)
        _renderDlgt();

      // Wait until rendering of scene is done, this also blocks EVR presenter, so the current frame data keeps untouched.
      _renderFinished.WaitOne(MAX_RENDER_TIME);
      return 0;
    }

    void RenderComplete(object sender, EventArgs e)
    {
      _renderFinished.Set();
    }

    #endregion
  }
}
