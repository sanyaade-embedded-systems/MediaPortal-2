#region Copyright (C) 2007-2010 Team MediaPortal

/*
    Copyright (C) 2007-2010 Team MediaPortal
    http://www.team-mediaportal.com

    This file is part of MediaPortal 2

    MediaPortal 2 is free software: you can redistribute it and/or modify
    it under the terms of the GNU General Public License as published by
    the Free Software Foundation, either version 3 of the License, or
    (at your option) any later version.

    MediaPortal 2 is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with MediaPortal 2.  If not, see <http://www.gnu.org/licenses/>.
*/

#endregion

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Dokan;
using MediaPortal.Core.MediaManagement.ResourceAccess;
using MediaPortal.Core.Services.MediaManagement.Settings;
using MediaPortal.Core.Settings;

namespace MediaPortal.Core.Services.MediaManagement
{
  public class ResourceMountingService : IResourceMountingService, DokanOperations
  {
    #region Consts

    public static string VOLUME_LABEL = "MediaPortal 2 resource access";

    protected const FileAttributes FILE_ATTRIBUTES = FileAttributes.Normal;
    protected const FileAttributes DIRECTORY_ATTRIBUTES = FileAttributes.ReadOnly | FileAttributes.NotContentIndexed | FileAttributes.Directory;

    #endregion

    #region Inner classes

    /// <summary>
    /// Handle for a virtual resource.
    /// </summary>
    /// <remarks>
    /// Multithreading safety is ensured by locking on the mounting service's synchronization object.
    /// </remarks>
    protected abstract class VirtualFileSystemResource
    {
      protected IResourceAccessor _resourceAccessor;
      protected ICollection<FileHandle> _fileHandles = new HashSet<FileHandle>();
      protected DateTime _creationTime;

      protected VirtualFileSystemResource(IResourceAccessor resourceAccessor)
      {
        _resourceAccessor = resourceAccessor;
        _creationTime = DateTime.Now;
      }

      public virtual void Dispose()
      {
        foreach (FileHandle handle in _fileHandles)
          handle.Cleanup();
        if (_resourceAccessor != null)
          _resourceAccessor.Dispose();
      }

      public IResourceAccessor ResourceAccessor
      {
        get { return _resourceAccessor;  }
      }

      public DateTime CreationTime
      {
        get { return _creationTime; }
      }

      public void AddFileHandle(FileHandle handle)
      {
        _fileHandles.Add(handle);
      }

      public void RemoveFileHandle(FileHandle handle)
      {
        _fileHandles.Remove(handle);
      }
    }

    /// <summary>
    /// Handle for a virtual file.
    /// </summary>
    protected class VirtualFile : VirtualFileSystemResource
    {
      public VirtualFile(IResourceAccessor resourceAccessor) :
          base(resourceAccessor) { }
    }

    protected abstract class VirtualBaseDirectory : VirtualFileSystemResource
    {
      protected VirtualBaseDirectory(IResourceAccessor resourceAccessor) : base(resourceAccessor) { }

      public abstract IDictionary<string, VirtualFileSystemResource> ChildResources { get; }
    }

    /// <summary>
    /// Handle for a virtual root directory.
    /// </summary>
    protected class VirtualRootDirectory : VirtualBaseDirectory
    {
      protected IDictionary<string, VirtualFileSystemResource> _children =
          new Dictionary<string, VirtualFileSystemResource>(StringComparer.InvariantCultureIgnoreCase);

      public VirtualRootDirectory() : base(null) { }

      public override void Dispose()
      {
        foreach (VirtualFileSystemResource resource in _children.Values)
          resource.Dispose();
        base.Dispose();
      }

      public override IDictionary<string, VirtualFileSystemResource> ChildResources
      {
        get { return _children; }
      }

      public void AddResource(string name, VirtualFileSystemResource resource)
      {
        _children.Add(name, resource);
      }

      public void RemoveResource(string name)
      {
        _children.Remove(name);
      }
    }

    /// <summary>
    /// Handle for a virtual directory.
    /// </summary>
    protected class VirtualDirectory : VirtualBaseDirectory
    {
      protected IDictionary<string, VirtualFileSystemResource> _children = null; // Lazily initialized

// ReSharper disable SuggestBaseTypeForParameter
      public VirtualDirectory(IFileSystemResourceAccessor resourceAccessor) :  base(resourceAccessor) { }
// ReSharper restore SuggestBaseTypeForParameter

      public override void Dispose()
      {
        if (_children != null)
          foreach (VirtualFileSystemResource resource in _children.Values)
            resource.Dispose();
        _children = null;
        base.Dispose();
      }

      public IFileSystemResourceAccessor Directory
      {
        get { return (IFileSystemResourceAccessor) _resourceAccessor; }
      }

      public override IDictionary<string, VirtualFileSystemResource> ChildResources
      {
        get
        {
          if (_children == null)
          {
            _children = new Dictionary<string, VirtualFileSystemResource>(StringComparer.InvariantCultureIgnoreCase);
            foreach (IFileSystemResourceAccessor childDirectoryAccessor in Directory.GetChildDirectories())
              _children[childDirectoryAccessor.ResourceName] = new VirtualDirectory(childDirectoryAccessor);
            foreach (IFileSystemResourceAccessor fileAccessor in Directory.GetFiles())
              _children[fileAccessor.ResourceName] = new VirtualFile(fileAccessor);
          }
          return _children;
        }
      }
    }

    /// <summary>
    /// File handle of a resource which is associated to a user file handle.
    /// </summary>
    protected class FileHandle
    {
      protected VirtualFileSystemResource _resource;
      protected Stream _stream = null;

      public FileHandle(VirtualFileSystemResource resource)
      {
        _resource = resource;
      }

      public VirtualFileSystemResource Resource
      {
        get { return _resource; }
      }

      public void Cleanup()
      {
        if (_stream != null)
          _stream.Dispose();
        _stream = null;
      }

      public Stream GetOrOpenStream()
      {
       if (_stream == null)
       {
         IResourceAccessor resourceAccessor = _resource.ResourceAccessor;
         _stream = resourceAccessor == null ? null : resourceAccessor.OpenRead();
       }
        return _stream;
      }
    }

    #endregion

    protected object _syncObj = new object();
    protected bool _started = false;
    protected char _driveLetter;
    protected VirtualRootDirectory _root = new VirtualRootDirectory();

    public ResourceMountingService()
    {
      _driveLetter = ReadDriveLetterFromSettings();
    }

    protected void Run()
    {
      DokanOptions opt = new DokanOptions {DriveLetter = _driveLetter, VolumeLabel = VOLUME_LABEL};
      DokanNet.DokanMain(opt, this);
    }

    protected VirtualFileSystemResource ParseFileName(string fileName)
    {
      if (fileName == "\\")
        return _root;
      if (!fileName.StartsWith("\\"))
        return null;
      string[] pathSegments = fileName.Split(new char[] {'\\'}, StringSplitOptions.RemoveEmptyEntries);
      VirtualFileSystemResource resource = _root;
      lock (_syncObj)
        foreach (string pathSegment in pathSegments)
        {
          if (resource is VirtualBaseDirectory)
          {
            if (!((VirtualBaseDirectory) resource).ChildResources.TryGetValue(pathSegment, out resource))
              return null;
          }
          else
            return null;
        }
      return resource;
    }

    protected char ReadDriveLetterFromSettings()
    {
      ISettingsManager settingsManager = ServiceRegistration.Get<ISettingsManager>();
      ResourceMountingSettings settings = settingsManager.Load<ResourceMountingSettings>();
      return settings.DriveLetter;
    }

    protected VirtualRootDirectory GetRootDirectory(string rootDirectoryName)
    {
        VirtualFileSystemResource directoryResource;
        if (!_root.ChildResources.TryGetValue(rootDirectoryName, out directoryResource))
          return null;
        return (VirtualRootDirectory) directoryResource;
    }

    #region IResourceMountingService implementation

    public char DriveLetter
    {
      get { return _driveLetter; }
    }

    public ICollection<string> RootDirectories
    {
      get
      {
        lock (_syncObj)
          return new List<string>(_root.ChildResources.Keys);
      }
    }

    public void Startup()
    {
      lock (_syncObj)
      {
        Thread thread = new Thread(Run) {Name = "Resource mounting service"};
        thread.Start();
        _started = true;
      }
    }

    public void Shutdown()
    {
      lock (_syncObj)
      {
        _root.Dispose();
        if (!_started)
          return;
      }
      DokanNet.DokanUnmount(DriveLetter);
    }

    public string CreateRootDirectory(string rootDirectoryName)
    {
      lock (_syncObj)
        _root.AddResource(rootDirectoryName, new VirtualRootDirectory());
      
      return _driveLetter + ":\\" + rootDirectoryName;
    }

    public void DisposeRootDirectory(string rootDirectoryName)
    {
      lock (_syncObj)
      {
        VirtualRootDirectory rootDirectory = GetRootDirectory(rootDirectoryName);
        if (rootDirectory == null)
          return;
        _root.RemoveResource(rootDirectoryName);
        rootDirectory.Dispose();
      }
    }

    public ICollection<IResourceAccessor> GetResources(string rootDirectoryName)
    {
      lock (_syncObj)
      {
        VirtualRootDirectory rootDirectory = GetRootDirectory(rootDirectoryName);
        if (rootDirectory == null)
          return null;
        ICollection<IResourceAccessor> result = new List<IResourceAccessor>();
        foreach (VirtualFileSystemResource resource in rootDirectory.ChildResources.Values)
          result.Add(resource.ResourceAccessor);
        return result;
      }
    }

    public string AddResource(string rootDirectoryName, IResourceAccessor resourceAccessor)
    {
      lock (_syncObj)
      {
        VirtualRootDirectory rootDirectory = GetRootDirectory(rootDirectoryName);
        if (rootDirectory == null)
          return null;
        string resourceName = resourceAccessor.ResourceName;
        IFileSystemResourceAccessor fsra = resourceAccessor as IFileSystemResourceAccessor;
        rootDirectory.AddResource(resourceName, fsra != null && fsra.IsDirectory ?
            (VirtualFileSystemResource) new VirtualDirectory(fsra) : new VirtualFile(resourceAccessor));
        return _driveLetter + ":\\" + rootDirectoryName + "\\" + resourceName;
      }
    }

    public void RemoveResource(string rootDirectoryName, IResourceAccessor resourceAccessor)
    {
      lock (_syncObj)
      {
        VirtualRootDirectory rootDirectory = GetRootDirectory(rootDirectoryName);
        if (rootDirectory == null)
          return;
        string resourceName = resourceAccessor.ResourceName;
        VirtualFileSystemResource toRemove;
        if (!rootDirectory.ChildResources.TryGetValue(resourceName, out toRemove))
          return;
        rootDirectory.ChildResources.Remove(resourceName);
        toRemove.Dispose();
      }
    }

    #endregion

    #region DokanOperations implementation

    public int CreateFile(string filename, FileAccess access, FileShare share, FileMode mode, FileOptions options, DokanFileInfo info)
    {
      lock (_syncObj)
      {
        VirtualFileSystemResource resource = ParseFileName(filename);
        if (resource == null)
          return -DokanNet.ERROR_FILE_NOT_FOUND;
        FileHandle handle = new FileHandle(resource);
        info.Context = handle;
        resource.AddFileHandle(handle);
        if (resource is VirtualBaseDirectory)
          info.IsDirectory = true; // Necessary for the Dokan driver to set this, see docs
        return 0;
      }
    }

    public int OpenDirectory(string filename, DokanFileInfo info)
    {
      lock (_syncObj)
      {
        VirtualFileSystemResource resource = ParseFileName(filename);
        if (resource == null || !(resource is VirtualBaseDirectory))
          return -DokanNet.ERROR_PATH_NOT_FOUND;
        FileHandle handle = new FileHandle(resource);
        info.Context = handle;
        resource.AddFileHandle(handle);
        return 0;
      }
    }

    public int CreateDirectory(string filename, DokanFileInfo info)
    {
      return -1;
    }

    public int Cleanup(string filename, DokanFileInfo info)
    {
      lock (_syncObj)
      {
        FileHandle handle = (FileHandle) info.Context;
        if (handle != null)
          handle.Cleanup();
        return 0;
      }
    }

    public int CloseFile(string filename, DokanFileInfo info)
    {
      lock (_syncObj)
      {
        FileHandle handle = (FileHandle) info.Context;
        if (handle != null)
          handle.Resource.RemoveFileHandle(handle);
        info.Context = null;
        return 0;
      }
    }

    public int ReadFile(string filename, byte[] buffer, ref uint readBytes, long offset, DokanFileInfo info)
    {
      Stream stream;
      lock (_syncObj)
      {
        FileHandle handle = (FileHandle) info.Context;
        if (handle == null)
          return -1;
        stream = handle.GetOrOpenStream();
        if (stream == null)
          return -1;
      }
      // Do the reading outside the lock - might block when not enough bytes are available
      stream.Seek(offset, SeekOrigin.Begin);
      readBytes = (uint) stream.Read(buffer, 0, buffer.Length);
      return 0;
    }

    public int WriteFile(string filename, byte[] buffer, ref uint writtenBytes, long offset, DokanFileInfo info)
    {
      return -1;
    }

    public int FlushFileBuffers(string filename, DokanFileInfo info)
    {
      return -1;
    }

    public int GetFileInformation(string filename, FileInformation fileinfo, DokanFileInfo info)
    {
      lock (_syncObj)
      {
        FileHandle handle = (FileHandle) info.Context;
        if (handle == null)
          return -1;
        VirtualFileSystemResource resource = handle.Resource;
        VirtualFile file = resource as VirtualFile;
        IResourceAccessor resourceAccessor = resource.ResourceAccessor;
        VirtualBaseDirectory directory = resource as VirtualBaseDirectory;
        fileinfo.FileName = filename;
        fileinfo.CreationTime = resource.CreationTime;
        fileinfo.LastAccessTime = resourceAccessor == null ? resource.CreationTime : resourceAccessor.LastChanged;
        fileinfo.LastWriteTime = resource.CreationTime; // When using DateTime.MinValue, the resource is not recognized
        if (file != null)
        {
          fileinfo.Attributes = FILE_ATTRIBUTES;
          fileinfo.Length = resourceAccessor == null ? 0 : resourceAccessor.Size;
          return 0;
        }
        if (directory != null)
        {
          fileinfo.Attributes = DIRECTORY_ATTRIBUTES;
          fileinfo.Length = 0;
          return 0;
        }
        return -1;
      }
    }

    public int FindFiles(string filename, ArrayList files, DokanFileInfo info)
    {
      lock (_syncObj)
      {
        FileHandle handle = (FileHandle) info.Context;
        VirtualBaseDirectory directory = handle == null ? null : handle.Resource as VirtualBaseDirectory;
        if (directory == null)
          return -1;
        foreach (KeyValuePair<string, VirtualFileSystemResource> entry in directory.ChildResources)
        {
          VirtualFileSystemResource resource = entry.Value;
          IResourceAccessor resourceAccessor = resource.ResourceAccessor;
          FileInformation fi = new FileInformation
            {
                Attributes = resource is VirtualFile ? FILE_ATTRIBUTES : DIRECTORY_ATTRIBUTES,
                CreationTime = resource.CreationTime,
                LastAccessTime = resourceAccessor == null ? resource.CreationTime : resourceAccessor.LastChanged,
                LastWriteTime = directory.CreationTime, // When using DateTime.MinValue, the resource is not recognized
                Length = resourceAccessor == null ? 0 : resourceAccessor.Size,
                FileName = entry.Key
            };
          files.Add(fi);
        }
        return 0;
      }
    }

    public int SetFileAttributes(string filename, FileAttributes attr, DokanFileInfo info)
    {
      return -1;
    }

    public int SetFileTime(string filename, DateTime ctime, DateTime atime, DateTime mtime, DokanFileInfo info)
    {
      return -1;
    }

    public int DeleteFile(string filename, DokanFileInfo info)
    {
      return -1;
    }

    public int DeleteDirectory(string filename, DokanFileInfo info)
    {
      return -1;
    }

    public int MoveFile(string filename, string newname, bool replace, DokanFileInfo info)
    {
      return -1;
    }

    public int SetEndOfFile(string filename, long length, DokanFileInfo info)
    {
      return -1;
    }

    public int SetAllocationSize(string filename, long length, DokanFileInfo info)
    {
      return -1;
    }

    public int LockFile(string filename, long offset, long length, DokanFileInfo info)
    {
      return 0;
    }

    public int UnlockFile(string filename, long offset, long length, DokanFileInfo info)
    {
      return 0;
    }

    public int GetDiskFreeSpace(ref ulong freeBytesAvailable, ref ulong totalBytes, ref ulong totalFreeBytes, DokanFileInfo info)
    {
      return 0;
    }

    // Function is called on system shutdown
    public int Unmount(DokanFileInfo info)
    {
      CloseFile(string.Empty, info);
      return 0;
    }

    #endregion
  }
}