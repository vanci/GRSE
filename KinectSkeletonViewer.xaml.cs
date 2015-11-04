//------------------------------------------------------------------------------
// <copyright file="KinectSkeletonViewer.xaml.cs" company="Microsoft">
//     Copyright (c) Microsoft Corporation.  All rights reserved.
// </copyright>
//------------------------------------------------------------------------------

namespace Microsoft.Samples.Kinect.WpfViewers
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.Windows;
    using System.Windows.Data;
    using System.Runtime.InteropServices;
    using Microsoft.Win32.SafeHandles;
    using Microsoft.Kinect;
    using System.Linq;
    using DTWGestureRecognition;
    using System.Windows.Controls;

    public enum ImageType
    {
        /// <summary>
        /// The Color Image
        /// </summary>
        Color,

        /// <summary>
        /// The Depth Image
        /// </summary>
        Depth,
    }

    /// <summary>
    /// Interaction logic for KinectSkeletonViewer.xaml
    /// </summary>
    public partial class KinectSkeletonViewer : KinectViewer
    {
        public static readonly DependencyProperty ShowBonesProperty =
            DependencyProperty.Register(
                "ShowBones",
                typeof(bool),
                typeof(KinectSkeletonViewer),
                new PropertyMetadata(true));

        public static readonly DependencyProperty ShowJointsProperty =
            DependencyProperty.Register(
                "ShowJoints",
                typeof(bool),
                typeof(KinectSkeletonViewer),
                new PropertyMetadata(true));

        public static readonly DependencyProperty ShowCenterProperty =
            DependencyProperty.Register(
                "ShowCenter",
                typeof(bool),
                typeof(KinectSkeletonViewer),
                new PropertyMetadata(true));

        public static readonly DependencyProperty ImageTypeProperty =
            DependencyProperty.Register(
                "ImageType",
                typeof(ImageType),
                typeof(KinectSkeletonViewer),
                new PropertyMetadata(ImageType.Color));

        private const int SkeletonCount = 6;
        private readonly List<KinectSkeleton> skeletonCanvases = new List<KinectSkeleton>(SkeletonCount);
        private readonly List<Dictionary<JointType, JointMapping>> jointMappings = new List<Dictionary<JointType, JointMapping>>();
        private Skeleton[] skeletonData;

        public KinectSkeletonViewer()
        {
            InitializeComponent();
            this.ShowJoints = true;
            this.ShowBones = true;
            this.ShowCenter = true;
            ConsoleManager.AllocConsole();
            Console.WriteLine("KinectSkeletonViewer initialized for " + ImageType);
        }

        public bool ShowBones
        {
            get { return (bool)GetValue(ShowBonesProperty); }
            set { SetValue(ShowBonesProperty, value); }
        }

        public bool ShowJoints
        {
            get { return (bool)GetValue(ShowJointsProperty); }
            set { SetValue(ShowJointsProperty, value); }
        }

        public bool ShowCenter
        {
            get { return (bool)GetValue(ShowCenterProperty); }
            set { SetValue(ShowCenterProperty, value); }
        }

        public ImageType ImageType
        {
            get { return (ImageType)GetValue(ImageTypeProperty); }
            set { SetValue(ImageTypeProperty, value); }
        }

        protected override void OnKinectSensorChanged(object sender, KinectSensorManagerEventArgs<KinectSensor> args)
        {
            if (null != args.OldValue)
            {
                args.OldValue.AllFramesReady -= this.KinectAllFramesReady;
                args.OldValue.SkeletonFrameReady -= SkeletonExtractSkeletonFrameReady;
            }

            if ((null != args.NewValue) && (KinectStatus.Connected == args.NewValue.Status))
            {
                args.NewValue.AllFramesReady += this.KinectAllFramesReady;
                if(ImageType.Color == ImageType)
                {
                    args.NewValue.SkeletonFrameReady -= SkeletonExtractSkeletonFrameReady;
                    args.NewValue.SkeletonFrameReady += SkeletonExtractSkeletonFrameReady;
                    prepareSkeletonExtract(args.NewValue);
                }
            }
        }

        /// <summary>
        /// Returns the 2D position of the provided 3D SkeletonPoint.
        /// The result will be in in either Color coordinate space or Depth coordinate space, depending on 
        /// the current value of this.ImageType.
        /// Only those parameters associated with the current ImageType will be used.
        /// </summary>
        /// <param name="sensor">The KinectSensor for which this mapping is being performed.</param>
        /// <param name="imageType">The target image type</param>
        /// <param name="renderSize">The target dimensions of the visualization</param>
        /// <param name="skeletonPoint">The source point to map</param>
        /// <param name="colorFormat">The format of the target color image, if imageType is Color</param>
        /// <param name="colorWidth">The width of the target color image, if the imageType is Color</param>
        /// <param name="colorHeight">The height of the target color image, if the imageType is Color</param>
        /// <param name="depthFormat">The format of the target depth image, if the imageType is Depth</param>
        /// <param name="depthWidth">The width of the target depth image, if the imageType is Depth</param>
        /// <param name="depthHeight">The height of the target depth image, if the imageType is Depth</param>
        /// <returns>Returns the 2D position of the provided 3D SkeletonPoint.</returns>
        private static Point Get2DPosition(
            KinectSensor sensor,
            ImageType imageType,
            Size renderSize,
            SkeletonPoint skeletonPoint,
            ColorImageFormat colorFormat,
            int colorWidth,
            int colorHeight,
            DepthImageFormat depthFormat,
            int depthWidth,
            int depthHeight)
        {
            try
            {
                switch (imageType)
                {
                    case ImageType.Color:
                        if (ColorImageFormat.Undefined != colorFormat)
                        {
                            var colorPoint = sensor.CoordinateMapper.MapSkeletonPointToColorPoint(skeletonPoint, colorFormat);

                            // map back to skeleton.Width & skeleton.Height
                            return new Point(
                                (int)(renderSize.Width * colorPoint.X / colorWidth),
                                (int)(renderSize.Height * colorPoint.Y / colorHeight));
                        }

                        break;
                    case ImageType.Depth:
                        if (DepthImageFormat.Undefined != depthFormat)
                        {
                            var depthPoint = sensor.CoordinateMapper.MapSkeletonPointToDepthPoint(skeletonPoint, depthFormat);

                            return new Point(
                                (int)(renderSize.Width * depthPoint.X / depthWidth),
                                (int)(renderSize.Height * depthPoint.Y / depthHeight));
                        }

                        break;
                }
            }
            catch (InvalidOperationException)
            {
                // The stream must have stopped abruptly
                // Handle this gracefully
            }

            return new Point();
        }

        private void KinectAllFramesReady(object sender, AllFramesReadyEventArgs e)
        {
            KinectSensor sensor = sender as KinectSensor;

            foreach (var skeletonCanvas in this.skeletonCanvases)
            {
                skeletonCanvas.Skeleton = null;
            }

            // Have we already been "shut down" by the user of this viewer, 
            // or has the SkeletonStream been disabled since this event was posted?
            if ((null == this.KinectSensorManager) || 
                (null == sensor) ||
                (null == sensor.SkeletonStream) ||
                !sensor.SkeletonStream.IsEnabled)
            {
                return;
            }

            bool haveSkeletonData = false;

            using (SkeletonFrame skeletonFrame = e.OpenSkeletonFrame())
            {
                if (skeletonFrame != null)
                {
                    if ((this.skeletonData == null) || (this.skeletonData.Length != skeletonFrame.SkeletonArrayLength))
                    {
                        this.skeletonData = new Skeleton[skeletonFrame.SkeletonArrayLength];
                    }

                    skeletonFrame.CopySkeletonDataTo(this.skeletonData);

                    haveSkeletonData = true;
                }
            }          

            if (haveSkeletonData)
            {
                ColorImageFormat colorFormat = ColorImageFormat.Undefined;
                int colorWidth = 0;
                int colorHeight = 0;

                DepthImageFormat depthFormat = DepthImageFormat.Undefined;
                int depthWidth = 0;
                int depthHeight = 0;

                switch (this.ImageType)
                {
                case ImageType.Color:
                    // Retrieve the current color format, from the frame if present, and from the sensor if not.
                    using (ColorImageFrame colorImageFrame = e.OpenColorImageFrame())
                    {
                        if (null != colorImageFrame)
                        {
                            colorFormat = colorImageFrame.Format;
                            colorWidth = colorImageFrame.Width;
                            colorHeight = colorImageFrame.Height;
                        }
                        else if (null != sensor.ColorStream)
                        {
                            colorFormat = sensor.ColorStream.Format;
                            colorWidth = sensor.ColorStream.FrameWidth;
                            colorHeight = sensor.ColorStream.FrameHeight;
                        }
                    }

                    break;
                case ImageType.Depth:
                    // Retrieve the current depth format, from the frame if present, and from the sensor if not.
                    using (DepthImageFrame depthImageFrame = e.OpenDepthImageFrame())
                    {
                        if (null != depthImageFrame)
                        {
                            depthFormat = depthImageFrame.Format;
                            depthWidth = depthImageFrame.Width;
                            depthHeight = depthImageFrame.Height;
                        }
                        else if (null != sensor.DepthStream)
                        {
                            depthFormat = sensor.DepthStream.Format;
                            depthWidth = sensor.DepthStream.FrameWidth;
                            depthHeight = sensor.DepthStream.FrameHeight;
                        }
                    }

                    break;
                }

                for (int i = 0; i < this.skeletonData.Length && i < this.skeletonCanvases.Count; i++)
                {
                    var skeleton = this.skeletonData[i];
                    var skeletonCanvas = this.skeletonCanvases[i];
                    var jointMapping = this.jointMappings[i];

                    jointMapping.Clear();

                    try
                    {
                        // Transform the data into the correct space
                        // For each joint, we determine the exact X/Y coordinates for the target view
                        foreach (Joint joint in skeleton.Joints)
                        {
                            Point mappedPoint = Get2DPosition(
                                sensor,
                                this.ImageType,
                                this.RenderSize,
                                joint.Position, 
                                colorFormat, 
                                colorWidth, 
                                colorHeight, 
                                depthFormat, 
                                depthWidth, 
                                depthHeight);

                            jointMapping[joint.JointType] = new JointMapping
                            {
                                Joint = joint,
                                MappedPoint = mappedPoint
                            };
                        }
                    }
                    catch (UnauthorizedAccessException)
                    {
                        // Kinect is no longer available.
                        return;
                    }

                    // Look up the center point
                    Point centerPoint = Get2DPosition(
                        sensor,
                        this.ImageType,
                        this.RenderSize,
                        skeleton.Position, 
                        colorFormat, 
                        colorWidth, 
                        colorHeight, 
                        depthFormat, 
                        depthWidth, 
                        depthHeight);

                    // Scale the skeleton thickness
                    // 1.0 is the desired size at 640 width
                    double scale = this.RenderSize.Width / 640;

                    skeletonCanvas.Skeleton = skeleton;
                    skeletonCanvas.JointMappings = jointMapping;
                    skeletonCanvas.Center = centerPoint;
                    skeletonCanvas.ScaleFactor = scale;
                }
            }
        }

        private void KinectSkeletonViewer_OnLoaded(object sender, RoutedEventArgs e)
        {
            // Build a set of Skeletons, and bind each of their control properties to those
            // exposed on this class so that changes are propagated.
            for (int i = 0; i < SkeletonCount; i++)
            {
                var skeletonCanvas = new KinectSkeleton();
                skeletonCanvas.ClipToBounds = true;

                var showBonesBinding = new Binding("ShowBones");
                showBonesBinding.Source = this;
                skeletonCanvas.SetBinding(KinectSkeleton.ShowBonesProperty, showBonesBinding);

                var showJointsBinding = new Binding("ShowJoints");
                showJointsBinding.Source = this;
                skeletonCanvas.SetBinding(KinectSkeleton.ShowJointsProperty, showJointsBinding);

                var showCenterBinding = new Binding("ShowCenter");
                showCenterBinding.Source = this;
                skeletonCanvas.SetBinding(KinectSkeleton.ShowCenterProperty, showCenterBinding);

                this.skeletonCanvases.Add(skeletonCanvas);
                this.jointMappings.Add(new Dictionary<JointType, JointMapping>());
                this.SkeletonCanvasPanel.Children.Add(skeletonCanvas);
            }

            if(ImageType.Depth == ImageType)
            {
                buttonAction1.Visibility = Visibility.Collapsed;
                buttonAction2.Visibility = Visibility.Collapsed;
                buttonAction3.Visibility = Visibility.Collapsed;
            }
        }

        #region Vriables and Handlers for recording skeleton samples or recognition
        private static Skeleton[] _FrameSkeletons;
        private ArrayList _video;
        private const int MinimumFrames = 6;
        private const int BufferSize = 32;
        private const int Ignore = 2;
        private bool _capturing;
        private DtwGestureRecognizer _dtw;
        private int _flipFlop = 0;

        private void prepareSkeletonExtract(KinectSensor sensor)
        {
            _dtw = new DtwGestureRecognizer(12, 0.6, 2, 2, 10);
            _video = new ArrayList();
            _FrameSkeletons = new Skeleton[sensor.SkeletonStream.FrameSkeletonArrayLength];
            Skeleton2DDataExtract.Skeleton2DdataCoordReady -= Skeleton2DdataCoordReady;
            Skeleton2DDataExtract.Skeleton2DdataCoordReady += Skeleton2DdataCoordReady;
            Console.WriteLine("Kinect ready for Skeleton extract");
        }
 
        // Handler to extra skeleton for recognition or recording sample
        private static void SkeletonExtractSkeletonFrameReady(object sender, SkeletonFrameReadyEventArgs e)
        {
            using (SkeletonFrame skeletonFrame = e.OpenSkeletonFrame())
            {
                if (skeletonFrame != null)
                {
                    skeletonFrame.CopySkeletonDataTo(_FrameSkeletons);
                    Skeleton data = (from s in _FrameSkeletons
                                     where s.TrackingState == SkeletonTrackingState.Tracked
                                     select s).FirstOrDefault();
                    Skeleton2DDataExtract.ProcessData(data);
                }
            }
        }

        private void Skeleton2DdataCoordReady(object sender, Skeleton2DdataCoordEventArgs a)
        {
            int currentFrameCount = _video.Count;
            // We need a sensible number of frames before we start attempting to match gestures against remembered sequences
            if (_video.Count > MinimumFrames && _capturing == false)
            {
                ////Debug.WriteLine("Reading and video.Count=" + video.Count);
                string s = _dtw.Recognize(_video);
                Console.WriteLine("Recognised as: " + s);
                if (!s.Contains("__UNKNOWN"))
                {
                    // There was no match so reset the buffer
                    _video = new ArrayList();
                }
            }
            // Ensures that we remember only the last x frames
            if (_video.Count > BufferSize)
            {
                // If we are currently capturing and we reach the maximum buffer size then automatically store
                if (_capturing)
                {
                   // TODO stop capturing and save the sample
                   // DtwStoreClick(null, null);
                }
                else
                {
                    // Remove the first frame in the buffer
                    _video.RemoveAt(0);
                }
            }
            // Decide which skeleton frames to capture. Only do so if the frames actually returned a number. 
            // For some reason my Kinect/PC setup didn't always return a double in range (i.e. infinity) even when standing completely within the frame.
            // TODO Weird. Need to investigate this
            if (!double.IsNaN(a.GetPoint(0).X))
            {
                // Optionally register only 1 frame out of every n
                _flipFlop = (_flipFlop + 1) % Ignore;
                if (_flipFlop == 0)
                {
                    _video.Add(a.GetCoords());
                }
            }
            // Update the debug window with Sequences information
            //dtwTextOutput.Text = _dtw.RetrieveText();
        }
        #endregion

        #region ConsoleManager
        internal static class ConsoleManager
        {
            // http://msdn.microsoft.com/en-us/library/ms681944(VS.85).aspx
            /// <summary>
            /// Allocates a new console for the calling process.
            /// </summary>
            /// <returns>nonzero if the function succeeds; otherwise, zero.</returns>
            /// <remarks>
            /// A process can be associated with only one console,
            /// so the function fails if the calling process already has a console.
            /// </remarks>
            [DllImport("kernel32.dll", SetLastError = true)]
            internal static extern int AllocConsole();

            // http://msdn.microsoft.com/en-us/library/ms683150(VS.85).aspx
            /// <summary>
            /// Detaches the calling process from its console.
            /// </summary>
            /// <returns>nonzero if the function succeeds; otherwise, zero.</returns>
            /// <remarks>
            /// If the calling process is not already attached to a console,
            /// the error code returned is ERROR_INVALID_PARAMETER (87).
            /// </remarks>
            [DllImport("kernel32.dll", SetLastError = true)]
            internal static extern int FreeConsole();
        }
        #endregion

        private void ButtonAction_Clicked(object sender, RoutedEventArgs e)
        {
            const string STOP = "Stop ";
            Button button = sender as Button;
            if( null != button )
            {
                Console.WriteLine(button.Content + " is clicked");
                string content = button.Content.ToString();
                if (content.StartsWith(STOP))
                {
                    button.Content = content.Substring(STOP.Length);
                    buttonAction1.IsEnabled = true;
                    buttonAction2.IsEnabled = true;
                    buttonAction3.IsEnabled = true;
                }
                else
                {
                    button.Content = STOP + content;
                    buttonAction1.IsEnabled = false;
                    buttonAction2.IsEnabled = false;
                    buttonAction3.IsEnabled = false;
                    button.IsEnabled = true;
                }
            }
            
        }
    }
}
