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

using System;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;
using MediaPortal.Common.General;
using MediaPortal.UI.SkinEngine.DirectX;
using SlimDX.Direct3D9;

namespace MediaPortal.UI.SkinEngine.SkinManagement
{                         
  public delegate void SkinResourcesChangedHandler(SkinResources newResources);

  /// <summary>
  /// Holds context variables which are used by the skin controls. This class may also be accessed from other plugins, for example video players.
  /// </summary>
  public static class SkinContext
  {
    #region Private fields

    private static readonly AbstractProperty _windowSizeProperty = new SProperty(typeof(Size), new Size(1920, 1080));
    private static readonly WeakEventMulticastDelegate _skinResourcesChangedDelegate = new WeakEventMulticastDelegate();
    private static SkinResources _skinResources = new Skin("[not initialized]"); // Avoid initialization issues. So we don't need to check "if SkinResources == null" every time
    private static Form _form;
    private static DateTime _frameRenderingStartTime;
    private static volatile Thread _renderThread = null;

    public static uint SystemTickCount;

    #endregion

    public enum RenderModeType
    {
      SleepForceImmediate,
      NoSleepPresentNone,
      NoSleepForceImmediate
    }

    public static event SkinResourcesChangedHandler SkinResourcesChanged
    {
      add { _skinResourcesChangedDelegate.Attach(value); }
      remove { _skinResourcesChangedDelegate.Detach(value); }
    }

    public static AbstractProperty WindowSizeProperty
    {
      get { return _windowSizeProperty; }
    }

    /// <summary>
    /// Returns the application window's extends. This is the maximum available space to render.
    /// </summary>
    public static Size WindowSize
    {
      get { return (Size) _windowSizeProperty.GetValue(); }
      internal set { _windowSizeProperty.SetValue(value); }
    }

    public static DateTime FrameRenderingStartTime
    {
      get { return _frameRenderingStartTime; }
      internal set { _frameRenderingStartTime = value; }
    }

    /// <summary>
    /// Gets or sets the Application's main windows form.
    /// </summary>
    public static Form Form
    {
      get { return _form; }
      internal set { _form = value; }
    }

    public static Thread RenderThread
    {
      get { return _renderThread; }
      internal set { _renderThread = value; }
    }

    /// <summary>
    /// Gets the DirectX device.
    /// </summary>
    public static DeviceEx Device
    {
      get { return GraphicsDevice.Device; }
    }

    /// <summary>
    /// Returns the Direct3D instance of the SkinEngine.
    /// </summary>
    public static Direct3DEx Direct3D
    {
      get { return MPDirect3D.Direct3D; }
    }

    /// <summary>
    /// Get or Sets different RenderModes (affects frame sync and present mode).
    /// </summary>
    public static RenderModeType RenderMode
    {
      get
      {
        return GraphicsDevice.RenderMode;
      }
      set
      {
        GraphicsDevice.RenderMode = value;
      }
    }

    /// <summary>
    /// Toggles between different RenderModes (affects frame sync and present mode).
    /// </summary>
    public static void NextRenderMode()
    {
      RenderMode = (RenderModeType)((((int)RenderMode)+1) % 3);
    }

    /// <summary>
    /// Exposes an event of the rendering process. It gets fired immediately after DeviceEx.BeginScene.
    /// </summary>
    public static event EventHandler DeviceSceneBegin
    {
      add { GraphicsDevice.DeviceSceneBegin += value; }
      remove { GraphicsDevice.DeviceSceneBegin -= value; }
    }

    /// <summary>
    /// Exposes an event of the rendering process. It gets fired immediately before DeviceEx.EndScene.
    /// </summary>
    public static event EventHandler DeviceSceneEnd
    {
      add { GraphicsDevice.DeviceSceneEnd += value; }
      remove { GraphicsDevice.DeviceSceneEnd -= value; }
    }

    /// <summary>
    /// Exposes an event of the rendering process. It gets fired immediately after DeviceEx.PresentEx.
    /// </summary>
    public static event EventHandler DeviceScenePresented
    {
      add { GraphicsDevice.DeviceScenePresented += value; }
      remove { GraphicsDevice.DeviceScenePresented -= value; }
    }

    /// <summary>
    /// Gets the back-buffer width of the DeviceEx.
    /// </summary>
    public static int BackBufferWidth
    {
      get { return GraphicsDevice.Width; }
    }

    /// <summary>
    /// Gets the back-buffer height of the DeviceEx.
    /// </summary>
    public static int BackBufferHeight
    {
      get { return GraphicsDevice.Height; }
    }

    /// <summary>
    /// Returns the current display mode used in the SkinEngine.
    /// </summary>
    public static DisplayMode CurrentDisplayMode
    {
      get
      {
        int ordinal = GraphicsDevice.Device.Capabilities.AdapterOrdinal;
        AdapterInformation adapterInfo = MPDirect3D.Direct3D.Adapters[ordinal];
        return adapterInfo.CurrentDisplayMode;
      }
    }

    /// <summary>
    /// Gets or sets the skin resources currently in use.
    /// A query to this resource collection will automatically fallback on the
    /// next resource collection in the priority chain. For example,
    /// if a requested resource is not present, it will fallback to the
    /// default theme/skin.
    /// </summary>
    public static SkinResources @SkinResources
    {
      get { return _skinResources; }
      internal set
      {
        _skinResources = value;
        _skinResourcesChangedDelegate.Fire(new object[] {_skinResources});
      }
    }
  }
}
