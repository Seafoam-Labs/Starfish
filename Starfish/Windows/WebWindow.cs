using Gtk;
using Starfish.Helpers;
using Starfish.Services;

namespace Starfish.Windows;

public class WebWindow(IPackageTraversalService packageTraversalService)
{
    private string _rootPackage = string.Empty;
    private Dictionary<string, List<string>> _dependencyMap = new();
    private Box _box = null!;
    private SpinButton _depthSpinner = null!;
    private Entry _searchEntry = null!;
    private readonly GskGraphWidget _graphWidget = new();

    private double _zoom = 1.0;

    private double _panX, _panY;
    private double _panStartX, _panStartY;

    private bool _showInverse;
    private bool _installOnly = true;

    private string? _hoverCandidate;
    private uint _hoverTimeoutId;

    private Revealer _packageInfoRevealer = null!;
    private Label _packageNameLabel = null!;
    private Label _packageVersionLabel = null!;
    private Label _packageDescriptionLabel = null!;
    private Label _packageSizeLabel = null!;
    private Label _packageRepoLabel = null!;

    public async Task InitializeAsync(string rootPackage, int depth)
    {
        _rootPackage = rootPackage;
        if (_searchEntry.GetText() != rootPackage)
        {
            _searchEntry.SetText(rootPackage);
        }

        if (_showInverse)
        {
            _dependencyMap = _installOnly 
                ? await packageTraversalService.FetchInverseFullDependencyPackageInformationInstalled(rootPackage, depth)
                : await packageTraversalService.FetchInverseFullDependencyPackageInformation(rootPackage, depth);
        }
        else
        {
            _dependencyMap = _installOnly
                ? await packageTraversalService.FetchFullDependencyPackageInformationInstalled(rootPackage, depth)
                : await packageTraversalService.FetchFullDependencyPackageInformation(rootPackage, depth);
        }
        _graphWidget.UpdateData(_rootPackage, _dependencyMap);
    }

    public Widget CreateWindow()
    {
        var builder = Builder.NewFromString(
            ResourceHelper.LoadUiFile("UiFiles/WebWindow.ui"), -1);

        _box = (Box)builder.GetObject("WebWindow")!;
        
        _searchEntry = (Entry)builder.GetObject("search_entry")!;
        _searchEntry.OnActivate += (sender, args) => {
            var pkg = _searchEntry.GetText();
            if (!string.IsNullOrWhiteSpace(pkg))
            {
                _ = InitializeAsync(pkg, (int)_depthSpinner.GetValue());
            }
        };

        _depthSpinner = (SpinButton)builder.GetObject("depth_spin")!;
        _depthSpinner.OnValueChanged += (sender, args) => {
            _ = InitializeAsync(_rootPackage, (int)sender.GetValue());
        };
        
     
        var allLabel = (Label)builder.GetObject("all_label")!;
        allLabel.SetText(_showInverse ? "Showing Inverse" : "Showing Forward");

        var showInverseBtn = (Button)builder.GetObject("show_inverse")!;
        showInverseBtn.OnClicked += (sender, args) => {
            _showInverse = true;
            allLabel.SetText("Showing Inverse");
            _ = InitializeAsync(_rootPackage, (int)_depthSpinner.GetValue());
        };
        
        var showForwardBtn = (Button)builder.GetObject("show_forward")!;
        showForwardBtn.OnClicked += (sender, args) => {
            _showInverse = false;
            allLabel.SetText("Showing Forward");
            _ = InitializeAsync(_rootPackage, (int)_depthSpinner.GetValue());
        };

        var invLabel = (Label)builder.GetObject("inverse_label")!;
        invLabel.SetText(_installOnly ? "Showing Installed Only" : "Showing All Dependencies");
        
        var installOnlyBtn = (Button)builder.GetObject("install_only")!;
        installOnlyBtn.OnClicked += (sender, args) =>
        {
            invLabel.SetText("Showing Installed Only");
            _installOnly = true;
            _ = InitializeAsync(_rootPackage, (int)_depthSpinner.GetValue());
        };
        
        var allOnlyBtn = (Button)builder.GetObject("all_only")!;
        allOnlyBtn.OnClicked += (sender, args) => {
            invLabel.SetText("Showing All Dependencies");
            _installOnly = false;
            _ = InitializeAsync(_rootPackage, (int)_depthSpinner.GetValue());
        };
        
        var resetPan = (Button)builder.GetObject("reset_pan")!;
        resetPan.OnClicked += (_, _) => {
          ResetPan();
        };

        var perfBtn = (CheckButton)builder.GetObject("performance_mode")!;
        perfBtn.OnToggled += (sender, _) => {
            _graphWidget.UsePerformanceShaders = sender.GetActive();
        };

        var lockHoverBtn = (CheckButton)builder.GetObject("lock_hover")!;
        lockHoverBtn.OnToggled += (sender, _) => {
            _graphWidget.LockHover = sender.GetActive();
        };

        _packageInfoRevealer = (Revealer)builder.GetObject("package_info_revealer")!;
        _packageNameLabel = (Label)builder.GetObject("package_name_label")!;
        _packageVersionLabel = (Label)builder.GetObject("package_version_label")!;
        _packageDescriptionLabel = (Label)builder.GetObject("package_description_label")!;
        _packageSizeLabel = (Label)builder.GetObject("package_size_label")!;
        _packageRepoLabel = (Label)builder.GetObject("package_repo_label")!;

        var closeInfoBtn = (Button)builder.GetObject("close_info_btn")!;
        closeInfoBtn.OnClicked += (_, _) =>
        {
            _packageInfoRevealer.SetRevealChild(false);
        };
        
        var oldOverlay = (Widget?)builder.GetObject("graph_overlay");
        var revealerParent = _packageInfoRevealer.GetParent();
        if (revealerParent != null)
        {
            switch (revealerParent)
            {
                case Box box:
                    box.Remove(_packageInfoRevealer);
                    break;
                case Overlay ov:
                    ov.RemoveOverlay(_packageInfoRevealer);
                    break;
            }
        }

        if (oldOverlay != null)
        {
            _box.Remove(oldOverlay);
        }
        
        _graphWidget.SetHexpand(true);
        _graphWidget.SetVexpand(true);

        var labelOverlay = DrawingArea.New();
        labelOverlay.CanTarget = false;
        labelOverlay.SetDrawFunc((_, cr, w, h) =>
        {
            _graphWidget.DrawLabels(cr, w, h);
        });

        var overlay = Overlay.New();
        overlay.SetHexpand(true);
        overlay.SetVexpand(true);
        overlay.SetChild(_graphWidget);
        overlay.AddOverlay(labelOverlay);
        overlay.AddOverlay(_packageInfoRevealer);

        _graphWidget.SetLabelOverlay(labelOverlay);

        _box.Prepend(overlay);

        var scroll = EventControllerScroll.New(EventControllerScrollFlags.Vertical);
        scroll.OnScroll += OnScroll;
        _graphWidget.AddController(scroll);

        var drag = GestureDrag.New();
        drag.Button = 3;
        drag.OnDragBegin += OnPanBegin;
        drag.OnDragUpdate += OnPanUpdate;
        _graphWidget.AddController(drag);

        var click = GestureClick.New();
        click.Button = 1;
        click.OnPressed += OnClick;
        _graphWidget.AddController(click);

        var motion = EventControllerMotion.New();
        motion.OnMotion += OnMotion;
        motion.OnLeave += OnLeave;
        _graphWidget.AddController(motion);
        
        return _box;
    }

    private void OnMotion(EventControllerMotion sender, EventControllerMotion.MotionSignalArgs args)
    {
        var name = _graphWidget.GetPackageAt(args.X, args.Y);
        if (name == _graphWidget.GetHoverNode())
        {
            if (_hoverTimeoutId != 0)
            {
                GLib.Functions.SourceRemove(_hoverTimeoutId);
                _hoverTimeoutId = 0;
            }

            _hoverCandidate = name;
            return;
        }

        if (name == _hoverCandidate) return;

        if (_hoverTimeoutId != 0)
        {
            GLib.Functions.SourceRemove(_hoverTimeoutId);
            _hoverTimeoutId = 0;
        }

        _hoverCandidate = name;
        if (name == null)
        {
            _graphWidget.SetHoverNode(null);
        }
        else
        {
            _hoverTimeoutId = GLib.Functions.TimeoutAdd(0, 200, () =>
            {
                _graphWidget.SetHoverNode(_hoverCandidate);
                _hoverTimeoutId = 0;
                return false;
            });
        }
    }

    private void OnLeave(EventControllerMotion sender, EventArgs args)
    {
        if (_hoverTimeoutId != 0)
        {
            GLib.Functions.SourceRemove(_hoverTimeoutId);
            _hoverTimeoutId = 0;
        }
        _hoverCandidate = null;
        _graphWidget.SetHoverNode(null);
    }

    private bool OnScroll(EventControllerScroll sender,
        EventControllerScroll.ScrollSignalArgs args)
    {
        _zoom = Math.Clamp(_zoom * (args.Dy > 0 ? 0.9 : 1.1), 0.05, 5.0);
        _graphWidget.SetTransform(_zoom, _panX, _panY);
        return true;
    }

    private void OnPanBegin(GestureDrag sender, GestureDrag.DragBeginSignalArgs args)
    {
        _panStartX = _panX;
        _panStartY = _panY;
    }

    private void OnPanUpdate(GestureDrag sender, GestureDrag.DragUpdateSignalArgs args)
    {
        _panX = _panStartX + args.OffsetX;
        _panY = _panStartY + args.OffsetY;
        _graphWidget.SetTransform(_zoom, _panX, _panY);
    }

    private void ResetPan()
    {
        _panX = 0;
        _panY = 0;
        _graphWidget.SetTransform(_zoom, _panX, _panY);
    }

    private void OnClick(GestureClick sender, GestureClick.PressedSignalArgs args)
    {
        var name = _graphWidget.GetPackageAt(args.X, args.Y);
        if (name == null)
        {
            _packageInfoRevealer.SetRevealChild(false);
            return;
        }
     
        _ = OnNodeClicked(name);
    }
    
    
    private async Task OnNodeClicked(string packageName)
    {
        var pkg = await packageTraversalService.GetPackageInfo(packageName);
        if (pkg == null) return;

        _packageNameLabel.SetText(pkg.Name);
        _packageVersionLabel.SetText($"Version: {pkg.Version}");
        _packageDescriptionLabel.SetText(pkg.Description);
        _packageSizeLabel.SetText($"Installed Size: {SizeFormatter.FormatSize(pkg.InstalledSize)}");
        _packageRepoLabel.SetText($"Repository: {pkg.Repository}");

        _packageInfoRevealer.SetRevealChild(true);
    }

    
    public void Dispose()
    {
    }
}