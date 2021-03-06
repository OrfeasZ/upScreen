﻿using System;
using System.Collections.Generic;
using System.Drawing;
using System.Net;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using System.IO;
using System.Threading;
using upScreenLib.LogConsole;

namespace upScreenLib
{
    public class CaptureControl
    {
        // Setup the upload thread
        private static readonly Thread tUpload = new Thread(UploadImage);
        // Event raised when the upload has been completed
        public static event EventHandler<EventArgs> UploadComplete;
        // Event raised when the given url cannot be processed correctly
        public static event EventHandler<EventArgs> UrlCaptureFailed;

        #region Capture Full Screen

        public static void CaptureFullScreen()
        {
            GetBackgroundImage();

            CapturedImage.Image = CapturedImage.Bmp;

            // Generate a random name for the file
            Common.Random = Common.RandomString(Common.Profile.FileLenght);
            CapturedImage.Name = Common.Random + Common.GetFormat();

            // Get the image link, copy (?) to clipboard
            CapturedImage.Link = Common.GetLink();
            if (Common.CopyLink) Clipboard.SetText(CapturedImage.Link);

            // If the user's account works, start uploading
            CheckStartUpload();
            // raise UploadComplete to start looking for software updates
            UploadComplete(null, EventArgs.Empty);
        }

        #endregion

        #region Capture Area

        public static void CaptureArea(Rectangle area)
        {
            var s_Bitmap = new Bitmap(SystemInformation.VirtualScreen.Width, SystemInformation.VirtualScreen.Height);

            using (var s_Graphics = Graphics.FromImage(s_Bitmap))
                s_Graphics.CopyFromScreen(area.X, area.Y, 0, 0, s_Bitmap.Size);

            CapturedImage.Image = s_Bitmap.Clone(new Rectangle(0, 0, area.Width, area.Height), s_Bitmap.PixelFormat);

            s_Bitmap.Dispose();

            // Generate a random name for the file
            Common.Random = Common.RandomString(Common.Profile.FileLenght);
            CapturedImage.Name = Common.Random + Common.GetFormat();

            // Get the image link, copy (?) to clipboard
            CapturedImage.Link = Common.GetLink();
            if (Common.CopyLink) Clipboard.SetText(CapturedImage.Link);

            // If the user's account works, start uploading
            CheckStartUpload();
            // raise UploadComplete to start looking for software updates
            UploadComplete(null, EventArgs.Empty);
        }

        #endregion

        #region Capture Window

        /// <summary>
        /// Find and capture the window 'behind' the specified point
        /// </summary>
        public static void CaptureWindow(Point p)
        {
            IntPtr window = ApiWrapper.Window.WindowFromPoint(p);

            if (window == IntPtr.Zero) return; 
            
            Log.Write(l.Debug, "Capturing window: {0}", ApiWrapper.Window.GetWindowText(window));                        

            // Find the window from the click point
            while (ApiWrapper.Window.IsChild(ApiWrapper.Window.GetParent(window), window))
                window = ApiWrapper.Window.GetParent(window);                
                
            // rectangle at the position and size of the selected window
            Rectangle windowRect = ApiWrapper.Window.GetWindowRect(window);           

            // find the height of the windows taskbar
            int tbheight = Screen.PrimaryScreen.Bounds.Height - Screen.PrimaryScreen.WorkingArea.Height;
                
            // fix for when the window height is maximum
            if (windowRect.Height + windowRect.Y + tbheight + windowRect.Y == Screen.PrimaryScreen.Bounds.Height)
            {
                windowRect.Y = 0;
                windowRect.Height = Screen.PrimaryScreen.WorkingArea.Height;
            }

            // Fix negative locations
            if (windowRect.Location.X < 0)
            {
                windowRect.Width = windowRect.Width + windowRect.X;
                windowRect.X = 0;
            }
            if (windowRect.Location.Y < 0)
            {
                windowRect.Height = windowRect.Height + windowRect.Y;
                windowRect.Y = 0;
            }
                
            // Crop the full-screen image at the position/dimmensions of the selected window
            CapturedImage.Image = Crop(CapturedImage.Bmp, windowRect);

            Common.OtherFormOpen = true;
            // Generate a random name for the file
            Common.Random = Common.RandomString(Common.Profile.FileLenght);
            CapturedImage.Name = Common.Random + Common.GetFormat();
                
            // Get the image link, copy (?) to clipboard
            CapturedImage.Link = Common.GetLink();
            if (Common.CopyLink) Clipboard.SetText(CapturedImage.Link);

            // If the user's account works, start uploading
            CheckStartUpload();
            // raise UploadComplete to start looking for software updates
            UploadComplete(null, EventArgs.Empty);
        }

        #endregion

        #region Capture from Arguements

        /// <summary>
        /// Used when a file path is passed as an arguement to upScreen (when uploading from the context menus)
        /// Loads the image file into CapturedImage.Image
        /// </summary>
        public static void CaptureFromArgs()
        {            
            var li = new List<string>(Profile.ArgFiles);
            foreach (string s in li)
            {
                // Load the image from the file path found in arguements
                CapturedImage.Image = Image.FromFile(s);
                CapturedImage.LocalPath = s;

                Common.OtherFormOpen = true;
                // Generate a random name for the file
                Common.Random = Common.RandomString(Common.Profile.FileLenght);
                CapturedImage.Name = Common.Random + Common.GetFormat(CapturedImage.Image.RawFormat);

                // Get the image link, copy (?) to clipboard
                CapturedImage.Link = Common.GetLink();
                if (Common.CopyLink) Clipboard.SetText(CapturedImage.Link);

                // If the user's account works, start uploading
                CheckStartUpload();
            }

            UploadComplete(null, EventArgs.Empty);
        }

        #endregion        

        #region Image Editing and Finalizing

        /// <summary>
        /// Crop the given image at the given rectangle's potiton/dimmensions
        /// </summary>
        /// <param name="img">The image to crop</param>
        /// <param name="rect">The rectangle that defines the crop starting point and dimmensions</param>
        /// <returns></returns>
        public static Image Crop(Image img, Rectangle rect)
        {
            var bmp = new Bitmap(img);
            Rectangle frect = FinalizeRectangle(rect);
            Image bmpCropped = bmp.Clone(frect, bmp.PixelFormat);
            return bmpCropped;
        }

        /// <summary>
        /// Check if the rectangle gets outside the screen boundaries
        /// </summary>
        private static Rectangle FinalizeRectangle(Rectangle rect)
        {
            if (rect.Location.X + rect.Size.Width > SystemInformation.VirtualScreen.Width)
                rect.Width = SystemInformation.VirtualScreen.Width - rect.Location.X;

            if (rect.Location.Y + rect.Size.Height > SystemInformation.VirtualScreen.Height)
                rect.Height = SystemInformation.VirtualScreen.Height - rect.Location.Y;

            return rect;
        }

        #endregion

        #region Image Uploading

        /// <summary>
        /// Called when the image has finished uploading. 
        /// Deletes the local file, opens the uploaded image in browser and closes upScreen
        /// </summary>
        public static void ImageUploaded(object sender = null, Starksoft.Net.Ftp.PutFileAsyncCompletedEventArgs e = null)
        {
            if (e != null)
                if (e.Cancelled)
                {
                    Log.Write(l.Debug, e.Error.Message);
                    return;
                }

            try
            {
                if (!Profile.FromFileMenu)
                    File.Delete(CapturedImage.LocalPath);

                Common.ViewInBrowser();
            }
            catch (Exception ex) { Log.Write(l.Error, ex.Message); }

            Profile.ArgFiles.Remove(CapturedImage.LocalPath);
            Common.ImageUploaded = Profile.ArgFiles.Count == 0;

            if (Common.KillApp)
                Common.KillOrWait();
        }

        /// <summary>
        /// Start the upload if the account-checking thread (tCheckAccount) has exited
        /// </summary>
        public static void CheckStartUpload()
        {
            if (Client.tCheckAccount.IsAlive)
                Common.IsImageCaptured = true;
            else
                DoStartUpload();
        }

        /// <summary>
        /// Start the thread that uploads the image
        /// </summary>
        public static void DoStartUpload()
        {
            CapturedImage.LocalPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"upScreen\" + CapturedImage.Name);
            CapturedImage.RemotePath = Common.Combine(Common.Profile.RemoteFolder, CapturedImage.Name);
            // Start the upload in a separate thread
            tUpload.Start();
        }

        /// <summary>
        /// upload dat screenshot
        /// </summary>
        private static void UploadImage()
        {
            try
            {
                Log.Write(l.Client, "lP: {0}", CapturedImage.LocalPath);
                CapturedImage.Image.Save(CapturedImage.LocalPath);

                CapturedImage.Link = Common.GetLink();
                if (Common.CopyLink)
                    Clipboard.SetText(CapturedImage.Link);
            }
            catch (Exception ex){ Log.Write(l.Error, ex.Message); }

            Client.UploadCapturedImage();
        }

        #endregion        

        #region Image fetching

        #region P/Invoke declarations

        [DllImport("gdi32.dll")]
        static extern bool BitBlt(IntPtr hdcDest, int xDest, int yDest, int
        wDest, int hDest, IntPtr hdcSource, int xSrc, int ySrc, CopyPixelOperation rop);
        [DllImport("user32.dll")]
        static extern bool ReleaseDC(IntPtr hWnd, IntPtr hDc);
        [DllImport("gdi32.dll")]
        static extern IntPtr DeleteDC(IntPtr hDc);
        [DllImport("gdi32.dll")]
        static extern IntPtr DeleteObject(IntPtr hDc);
        [DllImport("gdi32.dll")]
        static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int nWidth, int nHeight);
        [DllImport("gdi32.dll")]
        static extern IntPtr CreateCompatibleDC(IntPtr hdc);
        [DllImport("gdi32.dll")]
        static extern IntPtr SelectObject(IntPtr hdc, IntPtr bmp);
        [DllImport("user32.dll")]
        public static extern IntPtr GetDesktopWindow();
        [DllImport("user32.dll")]
        public static extern IntPtr GetWindowDC(IntPtr ptr);

        #endregion

        /// <summary>
        /// Capture the entire screen into CapturedImage.Bmp
        /// </summary>
        public static void GetBackgroundImage()
        {
            Size sz = SystemInformation.VirtualScreen.Size;
            IntPtr hDesk = GetDesktopWindow();
            IntPtr hSrce = GetWindowDC(hDesk);
            IntPtr hDest = CreateCompatibleDC(hSrce);
            IntPtr hbmp = CreateCompatibleBitmap(hSrce, sz.Width, sz.Height);
            IntPtr hOldbmp = SelectObject(hDest, hbmp);
            BitBlt(hDest, 0, 0, sz.Width, sz.Height, hSrce, 0, 0, CopyPixelOperation.SourceCopy | CopyPixelOperation.CaptureBlt);
            CapturedImage.Bmp = Image.FromHbitmap(hbmp);
            SelectObject(hDest, hOldbmp);
            DeleteObject(hbmp);
            DeleteDC(hDest);
            ReleaseDC(hDesk, hSrce);
        }

        /// <summary>
        /// Set CapturedImage.Image as the image/image file copied to clipboard
        /// </summary>
        public static void GetFromClipboard()
        {
            // Load the image from a copied image
            if (Clipboard.ContainsImage())
                CapturedImage.Image = Clipboard.GetImage();
            // Load the image from a copied image file
            else if (Clipboard.ContainsFileDropList())
                CapturedImage.Image = Image.FromFile(Clipboard.GetFileDropList()[0]);
            // Load the image from a copied image url
            else if (Common.ImageUrlInClipboard)
                CapturedImage.Image = GetFromUrl(Clipboard.GetText());

            // Prevent null reference exception
            if (CapturedImage.Image == null) return;

            Common.OtherFormOpen = true;
            // Generate a random name for the file
            Common.Random = Common.RandomString(Common.Profile.FileLenght);
            CapturedImage.Name = Common.Random + Common.GetFormat(CapturedImage.Image.RawFormat);

            // Get the image link, copy (?) to clipboard
            CapturedImage.Link = Common.GetLink();
            if (Common.CopyLink) Clipboard.SetText(CapturedImage.Link);

            // If the user's account works, start uploading
            CheckStartUpload();
            // raise UploadComplete to start looking for software updates
            UploadComplete(null, EventArgs.Empty);
        }

        /// <summary>
        /// Download and return image from url
        /// </summary>
        /// <param name="url">url to image</param>        
        private static Image GetFromUrl(string url)
        {
            var uri = new UriBuilder(url).Uri;
            try
            {
                var request = (HttpWebRequest)WebRequest.Create(uri);
                request.Method = "HEAD";
                request.ServicePoint.Expect100Continue = false;
                request.ContentType = "application/x-www-form-urlencoded";

                using (var response = request.GetResponse())
                {
                    var fileType = response.Headers[HttpResponseHeader.ContentType];
                    var allowedTypes = new List<string> { "image/png", "image/jpeg", "image/jpg", "image/gif" };

                    //Compare web file MIME to valid types
                    if (allowedTypes.Contains(fileType))
                    {
                        //Download file to MemoryStream
                        var imgBytes = new WebClient().DownloadData(uri);
                        var memStream = new MemoryStream(imgBytes);
                        return Image.FromStream(memStream);
                    }
                }
            }
            catch
            {
                // Something went wrong...
                UrlCaptureFailed(null, EventArgs.Empty);
            }
            return null;
        }

        #endregion
    }
}