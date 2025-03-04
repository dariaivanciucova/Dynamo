using System;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Windows.Threading;
using Dynamo.Graph.Nodes;
using Dynamo.Visualization;
using Dynamo.Wpf.ViewModels.Watch3D;
using HelixToolkit.Wpf.SharpDX;
using HelixToolkit.SharpDX.Core;
using SharpDX;
using Point = System.Windows.Point;

namespace Dynamo.Controls
{
    /// <summary>
    /// Interaction logic for WatchControl.xaml
    /// </summary>
    public partial class Watch3DView
    {
        #region private members

        private Point rightMousePoint;
        private Point3D prevCamera;
        private bool runUpdateClipPlane = false;

        #endregion

        #region public properties

        [Obsolete("Do not use! This will change its type in a future version of Dynamo.")]
        public Viewport3DX View
        {
            get { return watch_view; }
        }

        internal HelixWatch3DViewModel ViewModel { get; private set; }

        #endregion

        #region constructors

        public Watch3DView()
        {
            InitializeComponent();
            Loaded += ViewLoadedHandler;
            Unloaded += ViewUnloadedHandler;

        }

        #endregion

        #region event registration

        private void UnregisterEventHandlers()
        {
            UnregisterButtonHandlers();

            CompositionTarget.Rendering -= CompositionTargetRenderingHandler;

            if (ViewModel == null) return;

            UnRegisterViewEventHandlers();

            ViewModel.RequestCreateModels -= RequestCreateModelsHandler;
            ViewModel.RequestRemoveModels -= RequestRemoveModelsHandler;
            ViewModel.RequestViewRefresh -= RequestViewRefreshHandler;
            ViewModel.RequestClickRay -= GetClickRay;
            ViewModel.RequestCameraPosition -= GetCameraPosition;
            ViewModel.RequestZoomToFit -= ViewModel_RequestZoomToFit;
            this.DataContext = null;
            if(watch_view != null)
            {
                watch_view.Items.Clear();
                watch_view.DataContext = null;
                watch_view.Dispose();
            }
        
        }

        private void RegisterEventHandlers()
        {
            CompositionTarget.Rendering += CompositionTargetRenderingHandler;

            RegisterButtonHandlers();

            RegisterViewEventHandlers();

            ViewModel.RequestCreateModels += RequestCreateModelsHandler;
            ViewModel.RequestRemoveModels += RequestRemoveModelsHandler;
            ViewModel.RequestViewRefresh += RequestViewRefreshHandler;
            ViewModel.RequestClickRay += GetClickRay;
            ViewModel.RequestCameraPosition += GetCameraPosition;
            ViewModel.RequestZoomToFit += ViewModel_RequestZoomToFit;

            ViewModel.UpdateUpstream();
            ViewModel.OnWatchExecution();

        }

        private void RegisterButtonHandlers()
        {
            MouseLeftButtonDown += MouseButtonIgnoreHandler;
            MouseLeftButtonUp += MouseButtonIgnoreHandler;
            MouseRightButtonUp += view_MouseRightButtonUp;
            PreviewMouseRightButtonDown += view_PreviewMouseRightButtonDown;
        }

        private void RegisterViewEventHandlers()
        {
            watch_view.MouseDown += ViewModel.OnViewMouseDown;
            watch_view.MouseUp += WatchViewMouseUphandler;
            watch_view.MouseMove += ViewModel.OnViewMouseMove;
            watch_view.CameraChanged += WatchViewCameraChangedHandler;
          
        }

        private void WatchViewCameraChangedHandler(object sender, RoutedEventArgs e)
        {
            var view = sender as Viewport3DX;
            if (view != null)
            {
                e.Source = view.GetCameraPosition();
            }
            ViewModel.OnViewCameraChanged(sender, e);
        }

        private void WatchViewMouseUphandler(object sender, MouseButtonEventArgs e)
        {
            ViewModel.OnViewMouseUp(sender, e);
            //Call update on completion of user manipulation of the scene
            runUpdateClipPlane = true;
        }

        private void UnRegisterViewEventHandlers()
        {
            watch_view.MouseDown -= ViewModel.OnViewMouseDown;
            watch_view.MouseUp -= WatchViewMouseUphandler;
            watch_view.MouseMove -= ViewModel.OnViewMouseMove;
            watch_view.CameraChanged -= WatchViewCameraChangedHandler;
         }		         

        private void UnregisterButtonHandlers()
        {
            MouseLeftButtonDown -= MouseButtonIgnoreHandler;
            MouseLeftButtonUp -= MouseButtonIgnoreHandler;
            MouseRightButtonUp -= view_MouseRightButtonUp;
            PreviewMouseRightButtonDown -= view_PreviewMouseRightButtonDown;
        }

        #endregion

        #region event handlers

        private void ViewUnloadedHandler(object sender, RoutedEventArgs e)
        {
            UnregisterEventHandlers();
            Loaded -= ViewLoadedHandler;
            Unloaded -= ViewUnloadedHandler;
        }

        private void ViewLoadedHandler(object sender, RoutedEventArgs e)
        {
            ViewModel = DataContext as HelixWatch3DViewModel;

            if (ViewModel == null) return;

            RegisterEventHandlers();
        }

        private void ViewModel_RequestZoomToFit(BoundingBox bounds)
        {
            var prevcamDir = watch_view.Camera.LookDirection;
            watch_view.ZoomExtents(bounds.ToRect3D(.05));
            //if after a zoom the camera is in an undefined position or view direction, reset it.
            if(watch_view.Camera.Position.ToVector3().IsUndefined() || 
                watch_view.Camera.LookDirection.ToVector3().IsUndefined() || 
                watch_view.Camera.LookDirection.Length == 0)
            {
                watch_view.Camera.Position = prevCamera;
                watch_view.Camera.LookDirection = prevcamDir;
            }
        }

        private void RequestViewRefreshHandler()
        {
            View.InvalidateRender();
            //Call update to the clipping plane after the scene items are updated
            runUpdateClipPlane = true;
        }

        private void RequestCreateModelsHandler(RenderPackageCache packages, bool forceAsyncCall = false)
        {
            if (!forceAsyncCall && CheckAccess())
            {
                ViewModel.GenerateViewGeometryFromRenderPackagesAndRequestUpdate(packages);
            }
            else
            {
                Dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(() => ViewModel.GenerateViewGeometryFromRenderPackagesAndRequestUpdate(packages)));
            }
        }

        private void RequestRemoveModelsHandler(NodeModel node)
        {
            if (CheckAccess())
            {
                ViewModel.DeleteGeometryForNode(node);
            }
            else
            {
                Dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(() => ViewModel.DeleteGeometryForNode(node)));
            }
        }

        private void ThumbResizeThumbOnDragDeltaHandler(object sender, DragDeltaEventArgs e)
        {
            var yAdjust = ActualHeight + e.VerticalChange;
            var xAdjust = ActualWidth + e.HorizontalChange;

            if (xAdjust >= inputGrid.MinWidth)
            {
                Width = xAdjust;
            }

            if (yAdjust >= inputGrid.MinHeight)
            {
                Height = yAdjust;
            }
        }

        private void CompositionTargetRenderingHandler(object sender, EventArgs e)
        {
            // https://github.com/DynamoDS/Dynamo/issues/7295
            // This should not crash Dynamo when View is null
            try
            {
                //Do not call the clip plane update on the render loop if the camera is unchanged or
                //the user is manipulating the view with mouse.  Do run when queued by runUpdateClipPlane bool 
                if (runUpdateClipPlane || (!View.Camera.Position.Equals(prevCamera) && !View.IsMouseCaptured))
                {
                    ViewModel.UpdateNearClipPlane();
                    runUpdateClipPlane = false;
                }
                ViewModel.ComputeFrameUpdate();
                prevCamera = View.Camera.Position;
            }
            catch(Exception ex)
            {
                ViewModel.CurrentSpaceViewModel.DynamoViewModel.Model.Logger.Log(ex.ToString());
            }         
        }

        private void MouseButtonIgnoreHandler(object sender, MouseButtonEventArgs e)
        {
            e.Handled = false;
        }

        private void view_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            rightMousePoint = e.GetPosition(watch3D);
        }

        private void view_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            //if the mouse has moved, and this is a right click, we assume 
            // rotation. handle the event so we don't show the context menu
            // if the user wants the contextual menu they can click on the
            // node sidebar or top bar
            if (e.GetPosition(watch3D) != rightMousePoint)
            {
                e.Handled = true;
            }
        }

        #endregion

        private IRay GetClickRay(MouseEventArgs args)
        {
            var mousePos = args.GetPosition(this);

            var ray = View.Point2DToRay3D(new Point(mousePos.X, mousePos.Y));

            if (ray == null) return null;

            var position = new Point3D(0, 0, 0);
            var normal = new Vector3D(0, 0, 1);
            var pt3D = ray.PlaneIntersection(position, normal);

            if (pt3D == null) return null;

            return new Ray3(ray.Origin, ray.Direction);
        }

        private Point3D GetCameraPosition()
        {
            return View.GetCameraPosition();
        }
    }

    
}
