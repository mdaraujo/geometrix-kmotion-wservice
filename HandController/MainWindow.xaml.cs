using Microsoft.Kinect;
using Microsoft.Kinect.Input;
using System;
using System.ComponentModel;
using System.Runtime.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace HandController
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        /// <summary>
        /// Height of the dot which represents a pointer
        /// </summary>
        private const double DotHeight = 60;

        /// <summary>
        /// Width of the dot which represents a pointer
        /// </summary>
        private const double DotWidth = 60;
        
        /// <summary>
        /// A green brush
        /// </summary>
        private SolidColorBrush greenBrush = Brushes.Green;

        /// <summary>
        /// A red brush
        /// </summary>
        private SolidColorBrush redBrush = Brushes.Red;

        /// <summary>
        /// Shows more details about the pointer data
        /// </summary>
        private bool showDetails = true;

        /// <summary>
        /// Keeps track of last time, so we know when we get a new set of pointers. Pointer events fire multiple times per timestamp, based on how
        /// many pointers are present.
        /// </summary>
        private TimeSpan lastTime;
        
        /// <summary>
        /// Engaged hand.
        /// </summary>
        private Hand hand;
        
        /// <summary>
        /// Type of the engaged hand.
        /// </summary>
        private HandType engagedHandType;

        /// <summary>
        /// Id of the currently engaged body.
        /// </summary>
        private ulong engagedBodyId;
        
        /// <summary>
        /// Active Kinect sensor
        /// </summary>
        private KinectSensor kinectSensor = null;

        /// <summary>
        /// Reader for body frames
        /// </summary>
        private BodyFrameReader bodyFrameReader = null;

        /// <summary>
        /// Array for the bodies
        /// </summary>
        private Body[] bodies = null;

        /// <summary>
        /// Initializes an instance of the <see cref="KinectPointerPointSample"/> class.
        /// </summary>
        public MainWindow()
        {
            this.hand = new Hand();
            this.hand.closed = false;

            this.kinectSensor = KinectSensor.GetDefault();
            this.bodyFrameReader = this.kinectSensor.BodyFrameSource.OpenReader();
            this.kinectSensor.Open();

            this.Loaded += MainWindow_Loaded;
            this.Closing += MainWindow_Closing;
            this.InitializeComponent();
        }

        /// <summary>
        /// Execute start up tasks
        /// </summary>
        /// <param name="sender">object sending the event</param>
        /// <param name="e">event arguments</param>
        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // Listen to kinect pointer events
            KinectCoreWindow kinectCoreWindow = KinectCoreWindow.GetForCurrentThread();
            kinectCoreWindow.PointerMoved += kinectCoreWindow_PointerMoved;

            // Listen to body frames arrival
            if (this.bodyFrameReader != null)
            {
                this.bodyFrameReader.FrameArrived += this.Reader_FrameArrived;
            }
        }

        /// <summary>
        /// Execute shutdown tasks
        /// </summary>
        /// <param name="sender">object sending the event</param>
        /// <param name="e">event arguments</param>
        private void MainWindow_Closing(object sender, CancelEventArgs e)
        {
            if (this.bodyFrameReader != null)
            {
                // BodyFrameReader is IDisposable
                this.bodyFrameReader.Dispose();
                this.bodyFrameReader = null;
            }

            if (this.kinectSensor != null)
            {
                this.kinectSensor.Close();
                this.kinectSensor = null;
            }
        }

        /// <summary>
        /// Handles the body frame data arriving from the sensor
        /// </summary>
        /// <param name="sender">object sending the event</param>
        /// <param name="e">event arguments</param>
        private void Reader_FrameArrived(object sender, BodyFrameArrivedEventArgs e)
        {
            bool dataReceived = false;

            using (BodyFrame bodyFrame = e.FrameReference.AcquireFrame())
            {
                if (bodyFrame != null)
                {
                    if (this.bodies == null)
                    {
                        this.bodies = new Body[bodyFrame.BodyCount];
                    }

                    // The first time GetAndRefreshBodyData is called, Kinect will allocate each Body in the array.
                    // As long as those body objects are not disposed and not set to null in the array,
                    // those body objects will be re-used.
                    bodyFrame.GetAndRefreshBodyData(this.bodies);
                    dataReceived = true;
                }
            }

            if (dataReceived)
            {
                for (int i = 0; i < bodies.Length; i++)
                {
                    Body body = bodies[i];

                    if (body.IsTracked && body.TrackingId == engagedBodyId)
                    {
                        if (engagedHandType == HandType.LEFT)
                        {
                            processHandState(body.HandLeftState);
                        }
                        else if (engagedHandType == HandType.RIGHT)
                        {
                            processHandState(body.HandRightState);
                        }
                        else
                        {
                            processHandStatClosed(false);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Process the state of the currently engaged hand
        /// </summary>
        /// <param name="state">state of the currently engaged hand</param>
        public void processHandState(HandState state)
        {
            if (state == HandState.Closed)
            {
                processHandStatClosed(true);
            }
            else
            {
                processHandStatClosed(false);
            }
        }

        /// <summary>
        /// Update the closed state of the engaged hand, if it is not already in the updated state
        /// </summary>
        /// <param name="closed">indicates if the engaged hand is closed</param>
        public void processHandStatClosed(bool closed)
        {
            if (hand.closed != closed)
            {
                hand.closed = closed;
                RenderEngagedPointer();
            }
        }

        /// <summary>
        /// Handles kinect pointer events
        /// </summary>
        /// <param name="sender">the KinectCoreWindow</param>
        /// <param name="args">Kinect pointer args</param>
        private void kinectCoreWindow_PointerMoved(object sender, KinectPointerEventArgs args)
        {
            KinectPointerPoint kinectPointerPoint = args.CurrentPoint;
            if (lastTime == TimeSpan.Zero || lastTime != kinectPointerPoint.Properties.BodyTimeCounter)
            {
                lastTime = kinectPointerPoint.Properties.BodyTimeCounter;
                mainScreen.Children.Clear();
            }
            
            RenderPointer(kinectPointerPoint.Properties.IsEngaged,
                kinectPointerPoint.Position.X,
                kinectPointerPoint.Position.Y,
                kinectPointerPoint.Properties.BodyTrackingId,
                kinectPointerPoint.Properties.HandType);
        }

        /// <summary>
        /// Show pointer information
        /// </summary>
        /// <param name="isEngaged">is the pointer currently engaged</param>
        /// <param name="posX">location of the pointer on x axis ]0,1[</param>
        /// <param name="posY">location of the pointer on y axis ]0,1[</param>
        /// <param name="bodyTrackingId">id of the currently tracked body</param>
        /// <param name="handType">which handtype (left/right) of the user generated this pointer</param>
        private void RenderPointer(bool isEngaged, float posX, float posY, 
            ulong bodyTrackingId, HandType handType)
        {
            StackPanel cursor = null;
            if (cursor == null)
            {
                cursor = new StackPanel();
                mainScreen.Children.Add(cursor);
            }

            cursor.Children.Clear();
            var ellipseColor = isEngaged ? greenBrush : redBrush;

            StackPanel sp = new StackPanel()
            {
                Margin = new Thickness(-5, -5, 0, 0),
                Orientation = Orientation.Horizontal
            };
            sp.Children.Add(new Ellipse()
            {
                HorizontalAlignment = HorizontalAlignment.Left,
                Height = DotHeight,
                Width = DotWidth,
                Margin = new Thickness(5),
                Fill = ellipseColor
            });
            cursor.Children.Add(sp);

            if (showDetails)
            {
                cursor.Children.Add(new TextBlock() { Text = "Position: " + posX + ", " + posY });
                cursor.Children.Add(new TextBlock() { Text = "BodyTrackingId: " + bodyTrackingId });
                cursor.Children.Add(new TextBlock() { Text = "HandType: " + handType });

                if (isEngaged)
                {
                    cursor.Children.Add(new TextBlock() { Text = "Closed: " + hand.closed });
                }
            }

            Canvas.SetLeft(cursor, posX * mainScreen.ActualWidth - DotWidth / 2);
            Canvas.SetTop(cursor, posY * mainScreen.ActualHeight - DotHeight / 2);


            if (isEngaged)
            {
                hand.posX = posX;
                hand.posY = posY;
                engagedBodyId = bodyTrackingId;
                engagedHandType = handType;
            }
        }

        /// <summary>
        /// Renders the currently engaged hand pointer
        /// </summary>
        private void RenderEngagedPointer()
        {
            RenderPointer(true, hand.posX, hand.posY, engagedBodyId, engagedHandType);
        }

        /// <summary>
        /// User checked/unchecked the show details checkbox
        /// </summary>
        /// <param name="sender">the checkbox</param>
        /// <param name="e">the event args</param>
        private void details_Checked(object sender, RoutedEventArgs e)
        {
            showDetails = details.IsChecked.Value;
        }
    }

    /// <summary>
    /// Structure that represents the engaged hand info to be sent to the clients 
    /// (not realy needed in this debug application)
    /// </summary>
    [DataContract]
    internal class Hand
    {
        [DataMember]
        internal bool closed;

        [DataMember]
        internal float posX;

        [DataMember]
        internal float posY;
    }

}
