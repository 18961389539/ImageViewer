using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using ImageViewer.Core.Geometry;
using ImageViewer.Core.Measurements;
using ImageViewer.Localization;
using ImageViewer.Models;
using ImageViewer.Services;
using Microsoft.Win32;

namespace ImageViewer.Dialogs;

/// <summary>Interactive workbench for creating, evaluating, inspecting, and exporting associative measurements.</summary>
internal sealed class AdvancedMeasurementDialog : Window
{
    private readonly Func<IReadOnlyList<RoiBase>> _getRois;
    private readonly Func<double> _getPixelSize;
    private readonly Func<string> _getUnit;
    private readonly Func<CameraCalibration?> _getCalibration;
    private readonly Action<RoiBase, AdvancedMeasurementDefinition[]> _setDefinitions;
    private readonly Func<System.Windows.Media.Imaging.BitmapSource?>? _getBitmap;
    private readonly Func<string?>? _getImagePath;
    private readonly Func<WorkpieceCoordinateSystem?>? _getWorkpiece;
    private readonly ObservableCollection<SourceOption> _sourceOptions = [];
    private readonly ObservableCollection<SourceOption> _selectedSources = [];
    private readonly ObservableCollection<AdvancedMeasurementDefinition> _savedDefinitions = [];
    private readonly ObservableCollection<EvaluationRow> _evaluationRows = [];
    private readonly Dictionary<Guid, RoiBase> _definitionOwners = [];
    private readonly ComboBox _operationCombo;
    private readonly TextBox _nameTextBox;
    private readonly TextBox _parameterTextBox;
    private readonly TextBox _nominalXTextBox;
    private readonly TextBox _nominalYTextBox;
    private readonly TextBox _lowerLimitTextBox;
    private readonly TextBox _upperLimitTextBox;
    private readonly CheckBox _nominalInWorkpieceCheckBox;
    private readonly ListBox _availableSourcesList;
    private readonly ListBox _selectedSourcesList;
    private readonly ListBox _definitionsList;
    private readonly ListBox _resultsList;
    private readonly TextBlock _parameterLabel;
    private readonly TextBlock _parameterHintText;
    private readonly TextBlock _detailsText;
    private readonly Canvas _witnessCanvas;
    private readonly Canvas _samplesCanvas;
    private IReadOnlyList<RoiBase> _observedRois = Array.Empty<RoiBase>();
    private INotifyCollectionChanged? _observedCollection;
    private readonly List<INotifyCollectionChanged> _observedGeometryCollections = [];
    private bool _refreshQueued;
    private bool _disposed;

    /// <summary>Creates the live analysis workbench.</summary>
    public AdvancedMeasurementDialog(
        Func<IReadOnlyList<RoiBase>> getRois,
        Func<double> getPixelSize,
        Func<string> getUnit,
        Func<CameraCalibration?> getCalibration,
        Action<RoiBase, AdvancedMeasurementDefinition[]> setDefinitions,
        Func<System.Windows.Media.Imaging.BitmapSource?>? getBitmap = null,
        Func<string?>? getImagePath = null,
        Func<WorkpieceCoordinateSystem?>? getWorkpiece = null)
    {
        _getRois = getRois ?? throw new ArgumentNullException(nameof(getRois));
        _getPixelSize = getPixelSize ?? throw new ArgumentNullException(nameof(getPixelSize));
        _getUnit = getUnit ?? throw new ArgumentNullException(nameof(getUnit));
        _getCalibration = getCalibration ?? throw new ArgumentNullException(nameof(getCalibration));
        _setDefinitions = setDefinitions ?? throw new ArgumentNullException(nameof(setDefinitions));
        _getBitmap = getBitmap;
        _getImagePath = getImagePath;
        _getWorkpiece = getWorkpiece;

        Title = UiText.Get("AdvancedMeasurementDialogTitle");
        Width = 1160;
        Height = 760;
        MinWidth = 850;
        MinHeight = 560;
        ResizeMode = ResizeMode.CanResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = SystemColors.ControlBrush;
        AutomationProperties.SetName(this, Title);

        var root = new Grid { Margin = new Thickness(14) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var headingPanel = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
        headingPanel.Children.Add(new TextBlock
        {
            Text = UiText.Get("AdvancedMeasurementDialogHeading"),
            FontSize = 20,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 4)
        });
        headingPanel.Children.Add(new TextBlock
        {
            Text = UiText.Get("AdvancedMeasurementDialogDescription"),
            TextWrapping = TextWrapping.Wrap,
            Foreground = SystemColors.GrayTextBrush
        });
        root.Children.Add(headingPanel);

        var body = new Grid();
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(370) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetRow(body, 1);
        root.Children.Add(body);

        var editor = new Grid { Margin = new Thickness(0, 0, 14, 0) };
        editor.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        editor.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetColumn(editor, 0);
        body.Children.Add(editor);

        var editorContent = new StackPanel();
        editorContent.Children.Add(SectionHeader("AdvancedMeasurementOperationLabel"));
        _operationCombo = SetAutomationName(new ComboBox
        {
            ItemsSource = Enum.GetValues<AdvancedMeasurementKind>().Select(kind => new OperationOption(kind, UiText.Get(OperationResourceKey(kind)))).ToArray(),
            DisplayMemberPath = nameof(OperationOption.DisplayName),
            SelectedValuePath = nameof(OperationOption.Kind),
            Margin = new Thickness(0, 0, 0, 8)
        }, UiText.Get("AdvancedMeasurementOperationLabel"));
        _operationCombo.SelectionChanged += (_, _) => UpdateParameterLabel();
        editorContent.Children.Add(_operationCombo);

        _nameTextBox = AddTextField(editorContent, "AdvancedMeasurementNameLabel");
        var parameterRow = new Grid { Margin = new Thickness(0, 2, 0, 6) };
        parameterRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        parameterRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });
        _parameterLabel = new TextBlock
        {
            Text = UiText.Get("AdvancedMeasurementParameterLabel"),
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap
        };
        _parameterTextBox = SetAutomationName(new TextBox { Text = "1" }, UiText.Get("AdvancedMeasurementParameterLabel"));
        Grid.SetColumn(_parameterTextBox, 1);
        parameterRow.Children.Add(_parameterLabel);
        parameterRow.Children.Add(_parameterTextBox);
        editorContent.Children.Add(parameterRow);
        _parameterHintText = new TextBlock
        {
            Text = UiText.Get("AdvancedMeasurementParameterHint"),
            TextWrapping = TextWrapping.Wrap,
            Foreground = SystemColors.GrayTextBrush,
            Margin = new Thickness(2, 0, 2, 5),
            Visibility = Visibility.Collapsed
        };
        editorContent.Children.Add(_parameterHintText);

        var nominalGrid = new Grid { Margin = new Thickness(0, 2, 0, 6) };
        nominalGrid.ColumnDefinitions.Add(new ColumnDefinition());
        nominalGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(88) });
        nominalGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        nominalGrid.ColumnDefinitions.Add(new ColumnDefinition());
        nominalGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(88) });
        AddCompactField(nominalGrid, 0, 1, "AdvancedMeasurementNominalXLabel", out _nominalXTextBox);
        AddCompactField(nominalGrid, 3, 4, "AdvancedMeasurementNominalYLabel", out _nominalYTextBox);
        _nominalXTextBox.Text = "0";
        _nominalYTextBox.Text = "0";
        editorContent.Children.Add(nominalGrid);
        _nominalInWorkpieceCheckBox = SetAutomationName(new CheckBox
        {
            Content = UiText.Get("AdvancedMeasurementWorkpieceNominal"),
            Margin = new Thickness(0, 2, 0, 6)
        }, UiText.Get("AdvancedMeasurementWorkpieceNominal"));
        editorContent.Children.Add(_nominalInWorkpieceCheckBox);

        var limitGrid = new Grid { Margin = new Thickness(0, 2, 0, 6) };
        limitGrid.ColumnDefinitions.Add(new ColumnDefinition());
        limitGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(88) });
        limitGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        limitGrid.ColumnDefinitions.Add(new ColumnDefinition());
        limitGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(88) });
        AddCompactField(limitGrid, 0, 1, "AdvancedMeasurementLowerLimitLabel", out _lowerLimitTextBox);
        AddCompactField(limitGrid, 3, 4, "AdvancedMeasurementUpperLimitLabel", out _upperLimitTextBox);
        editorContent.Children.Add(limitGrid);

        editorContent.Children.Add(SectionHeader("AdvancedMeasurementAvailableSourcesLabel"));
        _availableSourcesList = SetAutomationName(new ListBox
        {
            ItemsSource = _sourceOptions,
            DisplayMemberPath = nameof(SourceOption.DisplayName),
            SelectionMode = SelectionMode.Single,
            Height = 100
        }, UiText.Get("AdvancedMeasurementAvailableSourcesLabel"));
        editorContent.Children.Add(_availableSourcesList);
        editorContent.Children.Add(new TextBlock
        {
            Text = UiText.Get("AdvancedMeasurementSourceHint"),
            TextWrapping = TextWrapping.Wrap,
            Foreground = SystemColors.GrayTextBrush,
            Margin = new Thickness(2, 4, 2, 4)
        });

        var sourceButtons = new WrapPanel { Margin = new Thickness(0, 4, 0, 4) };
        sourceButtons.Children.Add(CreateButton("AdvancedMeasurementSourceAdd", AddSelectedSource));
        sourceButtons.Children.Add(CreateButton("AdvancedMeasurementRefreshSources", RefreshSourceInventory));
        editorContent.Children.Add(sourceButtons);

        editorContent.Children.Add(SectionHeader("AdvancedMeasurementSourcesLabel"));
        _selectedSourcesList = SetAutomationName(new ListBox
        {
            ItemsSource = _selectedSources,
            DisplayMemberPath = nameof(SourceOption.DisplayName),
            SelectionMode = SelectionMode.Single,
            Height = 88
        }, UiText.Get("AdvancedMeasurementSourcesLabel"));
        editorContent.Children.Add(_selectedSourcesList);
        var orderButtons = new WrapPanel { Margin = new Thickness(0, 4, 0, 6) };
        orderButtons.Children.Add(CreateButton("AdvancedMeasurementSourceRemove", RemoveSelectedSource));
        orderButtons.Children.Add(CreateButton("AdvancedMeasurementSourceUp", () => MoveSelectedSource(-1)));
        orderButtons.Children.Add(CreateButton("AdvancedMeasurementSourceDown", () => MoveSelectedSource(1)));
        editorContent.Children.Add(orderButtons);

        var inputScroll = new ScrollViewer
        {
            Content = editorContent,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
        editor.Children.Add(inputScroll);

        _definitionsList = SetAutomationName(new ListBox
        {
            ItemsSource = _savedDefinitions,
            DisplayMemberPath = nameof(AdvancedMeasurementDefinition.Name),
            Height = 112
        }, UiText.Get("AdvancedMeasurementSourcesLabel"));
        Grid.SetRow(_definitionsList, 1);
        editor.Children.Add(_definitionsList);

        var createButtons = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        createButtons.Children.Add(CreateButton("AdvancedMeasurementAdd", AddOrUpdateDefinition, isDefault: true));
        createButtons.Children.Add(CreateButton("AdvancedMeasurementNew", ClearDefinitionEditor));
        createButtons.Children.Add(CreateButton("AdvancedMeasurementDelete", DeleteSelectedDefinition));
        Grid.SetRow(createButtons, 2);
        editor.RowDefinitions.Insert(1, new RowDefinition { Height = GridLength.Auto });
        editor.Children.Add(createButtons);
        _definitionsList.Margin = new Thickness(0, 6, 0, 0);

        var analysis = new Grid();
        analysis.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        analysis.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(analysis, 1);
        body.Children.Add(analysis);

        var resultsPanel = new DockPanel { LastChildFill = true };
        var resultsHeading = SectionHeader("AdvancedMeasurementResultsLabel");
        DockPanel.SetDock(resultsHeading, Dock.Top);
        resultsPanel.Children.Add(resultsHeading);
        _resultsList = SetAutomationName(new ListBox
        {
            ItemsSource = _evaluationRows,
            DisplayMemberPath = nameof(EvaluationRow.Summary)
        }, UiText.Get("AdvancedMeasurementResultsLabel"));
        _resultsList.SelectionChanged += (_, _) => ShowSelectedEvaluation();
        resultsPanel.Children.Add(_resultsList);
        _detailsText = SetAutomationName(new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 8, 0, 0)
        }, UiText.Get("AdvancedMeasurementResultsLabel"));
        var detailsScroll = new ScrollViewer
        {
            Content = _detailsText,
            MaxHeight = 180,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Margin = new Thickness(0, 8, 0, 0)
        };
        DockPanel.SetDock(detailsScroll, Dock.Bottom);
        resultsPanel.Children.Add(detailsScroll);
        Grid.SetRow(resultsPanel, 0);
        analysis.Children.Add(resultsPanel);

        var previewPanel = new Grid { Margin = new Thickness(0, 14, 0, 0) };
        previewPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        previewPanel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        previewPanel.ColumnDefinitions.Add(new ColumnDefinition());
        previewPanel.ColumnDefinitions.Add(new ColumnDefinition());
        var witnessHeader = SectionHeader("AdvancedMeasurementPreviewLabel");
        Grid.SetColumn(witnessHeader, 0);
        previewPanel.Children.Add(witnessHeader);
        var samplesHeader = SectionHeader("AdvancedMeasurementSamplesLabel");
        Grid.SetColumn(samplesHeader, 1);
        previewPanel.Children.Add(samplesHeader);
        _witnessCanvas = CreatePreviewCanvas();
        _samplesCanvas = CreatePreviewCanvas();
        Grid.SetRow(_witnessCanvas, 1);
        Grid.SetRow(_samplesCanvas, 1);
        Grid.SetColumn(_samplesCanvas, 1);
        previewPanel.Children.Add(_witnessCanvas);
        previewPanel.Children.Add(_samplesCanvas);
        Grid.SetRow(previewPanel, 1);
        analysis.Children.Add(previewPanel);

        var footer = new DockPanel { Margin = new Thickness(0, 12, 0, 0) };
        var leftButtons = new WrapPanel();
        leftButtons.Children.Add(CreateButton("AdvancedMeasurementRefresh", EvaluateAll));
        leftButtons.Children.Add(CreateButton("AdvancedMeasurementExport", ExportCsvAsync));
        DockPanel.SetDock(leftButtons, Dock.Left);
        footer.Children.Add(leftButtons);
        var closeButton = CreateButton("AdvancedMeasurementClose", Close);
        closeButton.IsCancel = true;
        closeButton.HorizontalAlignment = HorizontalAlignment.Right;
        DockPanel.SetDock(closeButton, Dock.Right);
        footer.Children.Add(closeButton);
        Grid.SetRow(footer, 2);
        root.Children.Add(footer);

        Content = root;
        _operationCombo.SelectedIndex = 0;
        _definitionsList.SelectionChanged += (_, _) => LoadSelectedDefinition();
        Closed += OnClosed;
        RefreshSourceInventory();
        EvaluateAll();
    }

    private void RefreshSourceInventory()
    {
        DetachRoiNotifications();
        _observedRois = _getRois() ?? Array.Empty<RoiBase>();
        foreach (RoiBase roi in _observedRois)
        {
            roi.PropertyChanged += OnRoiPropertyChanged;
            if (roi is PolygonRoi polygon && polygon.Points is INotifyCollectionChanged polygonPoints)
            {
                polygonPoints.CollectionChanged += OnRoiCollectionChanged;
                _observedGeometryCollections.Add(polygonPoints);
            }
            else if (roi is PolylineRoi polyline && polyline.Points is INotifyCollectionChanged polylinePoints)
            {
                polylinePoints.CollectionChanged += OnRoiCollectionChanged;
                _observedGeometryCollections.Add(polylinePoints);
            }
        }

        _observedCollection = _observedRois as INotifyCollectionChanged;
        if (_observedCollection is not null)
        {
            _observedCollection.CollectionChanged += OnRoiCollectionChanged;
        }

        Guid? selectedDefinitionId = (_definitionsList.SelectedItem as AdvancedMeasurementDefinition)?.Id;
        _sourceOptions.Clear();
        _savedDefinitions.Clear();
        _definitionOwners.Clear();
        foreach (RoiBase roi in _observedRois)
        {
            _sourceOptions.Add(new SourceOption(roi.MeasurementId, roi.DisplayName, roi, null));
            foreach (AdvancedMeasurementDefinition definition in roi.AdvancedMeasurements)
            {
                _definitionOwners[definition.Id] = roi;
                _savedDefinitions.Add(definition);
            }
        }

        foreach (AdvancedMeasurementDefinition definition in AdvancedMeasurementService.EnumerateDefinitions(_observedRois))
        {
            RoiBase? owner = _definitionOwners.GetValueOrDefault(definition.Id);
            string display = UiText.Format("AdvancedMeasurementSourceLine", definition.Name, owner?.DisplayName ?? string.Empty);
            _sourceOptions.Add(new SourceOption(definition.Id, display, null, definition));
        }

        if (selectedDefinitionId is Guid id)
        {
            _definitionsList.SelectedItem = _savedDefinitions.FirstOrDefault(item => item.Id == id);
        }

        UpdateSelectedSourceLabels();
    }

    private void AddSelectedSource()
    {
        if (_availableSourcesList.SelectedItem is not SourceOption option || _selectedSources.Any(item => item.Id == option.Id))
        {
            return;
        }

        _selectedSources.Add(option);
        _selectedSourcesList.SelectedItem = option;
    }

    private void RemoveSelectedSource()
    {
        if (_selectedSourcesList.SelectedItem is SourceOption option)
        {
            _selectedSources.Remove(option);
        }
    }

    private void MoveSelectedSource(int offset)
    {
        if (_selectedSourcesList.SelectedItem is not SourceOption option)
        {
            return;
        }

        int index = _selectedSources.IndexOf(option);
        int destination = index + offset;
        if (destination < 0 || destination >= _selectedSources.Count)
        {
            return;
        }

        _selectedSources.Move(index, destination);
        _selectedSourcesList.SelectedItem = option;
    }

    private void AddOrUpdateDefinition()
    {
        if (_operationCombo.SelectedItem is not OperationOption operation
            || !TryParseDouble(_parameterTextBox.Text, out double parameter)
            || !TryParseDouble(_nominalXTextBox.Text, out double nominalX)
            || !TryParseDouble(_nominalYTextBox.Text, out double nominalY)
            || !TryParseOptionalDouble(_lowerLimitTextBox.Text, out double? lowerLimit)
            || !TryParseOptionalDouble(_upperLimitTextBox.Text, out double? upperLimit))
        {
            ShowWarning("AdvancedMeasurementInvalidInput");
            return;
        }

        if (string.IsNullOrWhiteSpace(_nameTextBox.Text) || _selectedSources.Count == 0
            || (lowerLimit is double lower && upperLimit is double upper && lower > upper))
        {
            ShowWarning("AdvancedMeasurementInvalidInput");
            return;
        }

        AdvancedMeasurementKind kind = operation.Kind;
        AdvancedMeasurementDefinition? existing = _definitionsList.SelectedItem as AdvancedMeasurementDefinition;
        var definition = new AdvancedMeasurementDefinition
        {
            Id = existing?.Id ?? Guid.NewGuid(),
            Name = _nameTextBox.Text.Trim(),
            Kind = kind,
            Sources = _selectedSources.Select(source => source.Id).ToArray(),
            Parameter = parameter,
            NominalX = nominalX,
            NominalY = nominalY,
            NominalInWorkpieceCoordinates = _nominalInWorkpieceCheckBox.IsChecked == true,
            LowerLimit = lowerLimit,
            UpperLimit = upperLimit
        };

        RoiBase? owner = existing is not null && _definitionOwners.TryGetValue(existing.Id, out RoiBase? existingOwner)
            ? existingOwner
            : FindDefinitionOwner(definition.Sources) ?? (_observedRois.Count > 0 ? _observedRois[0] : null);
        if (owner is null)
        {
            ShowWarning("AdvancedMeasurementNoSources");
            return;
        }

        AdvancedMeasurementDefinition[] ownerDefinitions = owner.AdvancedMeasurements
            .Where(item => existing is null || item.Id != existing.Id)
            .Select(item => item.DeepClone())
            .Append(definition)
            .ToArray();
        _setDefinitions(owner, ownerDefinitions);
        _definitionsList.SelectedItem = definition;
        RefreshSourceInventory();
        _definitionsList.SelectedItem = _savedDefinitions.FirstOrDefault(item => item.Id == definition.Id);
    }

    private void DeleteSelectedDefinition()
    {
        if (_definitionsList.SelectedItem is not AdvancedMeasurementDefinition selected
            || !_definitionOwners.TryGetValue(selected.Id, out RoiBase? owner))
        {
            return;
        }

        _setDefinitions(owner, [.. owner.AdvancedMeasurements.Where(item => item.Id != selected.Id).Select(item => item.DeepClone())]);
        RefreshSourceInventory();
    }

    private void LoadSelectedDefinition()
    {
        if (_definitionsList.SelectedItem is not AdvancedMeasurementDefinition definition)
        {
            return;
        }

        _operationCombo.SelectedValue = definition.Kind;
        _nameTextBox.Text = definition.Name;
        _parameterTextBox.Text = Format(definition.Parameter);
        _nominalXTextBox.Text = Format(definition.NominalX);
        _nominalYTextBox.Text = Format(definition.NominalY);
        _lowerLimitTextBox.Text = definition.LowerLimit is double lower ? Format(lower) : string.Empty;
        _upperLimitTextBox.Text = definition.UpperLimit is double upper ? Format(upper) : string.Empty;
        _nominalInWorkpieceCheckBox.IsChecked = definition.NominalInWorkpieceCoordinates;
        _selectedSources.Clear();
        foreach (Guid sourceId in definition.Sources)
        {
            SourceOption? source = _sourceOptions.FirstOrDefault(item => item.Id == sourceId);
            if (source is not null)
            {
                _selectedSources.Add(source);
            }
        }
    }

    private void ClearDefinitionEditor()
    {
        _definitionsList.SelectedItem = null;
        _nameTextBox.Clear();
        _operationCombo.SelectedIndex = 0;
        _parameterTextBox.Text = "1";
        _nominalXTextBox.Text = "0";
        _nominalYTextBox.Text = "0";
        _lowerLimitTextBox.Clear();
        _upperLimitTextBox.Clear();
        _nominalInWorkpieceCheckBox.IsChecked = false;
        _selectedSources.Clear();
    }

    internal void BeginMeasurement(AdvancedMeasurementKind kind, RoiBase? initialSource)
    {
        RefreshSourceInventory();
        ClearDefinitionEditor();
        _operationCombo.SelectedValue = kind;
        _nameTextBox.Text = UiText.Get(OperationResourceKey(kind));
        if (initialSource != null)
        {
            SourceOption? source = _sourceOptions.FirstOrDefault(option => option.Id == initialSource.MeasurementId);
            if (source != null) _selectedSources.Add(source);
        }
    }

    private void EvaluateAll()
    {
        IReadOnlyList<RoiBase> rois = _getRois();
        IReadOnlyList<AdvancedMeasurementEvaluation> results;
        try
        {
            results = AdvancedMeasurementService.EvaluateAll(rois, _getPixelSize(), _getUnit(), _getCalibration(), _getBitmap?.Invoke(), _getWorkpiece?.Invoke());
        }
        catch (Exception exception)
        {
            _evaluationRows.Clear();
            _detailsText.Text = UiText.Format("AdvancedMeasurementEvaluateFailed", exception.Message);
            ClearPreview(_witnessCanvas, "AdvancedMeasurementNoPreview");
            ClearPreview(_samplesCanvas, "AdvancedMeasurementNoSamples");
            return;
        }

        Dictionary<Guid, AdvancedMeasurementDefinition> definitions = AdvancedMeasurementService.EnumerateDefinitions(rois)
            .GroupBy(item => item.Id)
            .ToDictionary(group => group.Key, group => group.First());
        Guid? selectedId = (_resultsList.SelectedItem as EvaluationRow)?.Evaluation.Id;
        _evaluationRows.Clear();
        foreach (AdvancedMeasurementEvaluation evaluation in results)
        {
            if (definitions.TryGetValue(evaluation.Id, out AdvancedMeasurementDefinition? definition))
            {
                _evaluationRows.Add(new EvaluationRow(definition, evaluation, FormatSummary(definition, evaluation)));
            }
        }

        _resultsList.SelectedItem = selectedId is Guid id
            ? _evaluationRows.FirstOrDefault(row => row.Evaluation.Id == id)
            : _evaluationRows.FirstOrDefault();
        ShowSelectedEvaluation();
    }

    private string FormatSummary(AdvancedMeasurementDefinition definition, AdvancedMeasurementEvaluation evaluation)
    {
        string status = evaluation.Success
            ? evaluation.WithinLimits switch
            {
                true => UiText.Get("AdvancedMeasurementStatusWithinLimits"),
                false => UiText.Get("AdvancedMeasurementStatusOutOfLimits"),
                null => UiText.Get("AdvancedMeasurementStatusNoLimits")
            }
            : UiText.Get("AdvancedMeasurementStatusFailure");
        string primary = evaluation.Metrics.FirstOrDefault() is AdvancedMeasurementMetric metric
            ? FormatMetric(metric)
            : UiText.Get("AdvancedMeasurementNoSamples");
        return UiText.Format("AdvancedMeasurementResultRow", definition.Name, status, primary);
    }

    private string FormatMetric(AdvancedMeasurementMetric metric)
    {
        string unit = metric.Unit ?? (metric.LengthPower == 0 ? string.Empty : _getUnit());
        if (metric.Unit is null && metric.LengthPower == 2 && !string.IsNullOrWhiteSpace(unit))
        {
            unit += "²";
        }

        return UiText.Format("AdvancedMeasurementMetricRow", UiText.Get(metric.Name), metric.Value, unit);
    }

    private void ShowSelectedEvaluation()
    {
        if (_resultsList.SelectedItem is not EvaluationRow row)
        {
            _detailsText.Text = UiText.Get("AdvancedMeasurementNoDefinitions");
            ClearPreview(_witnessCanvas, "AdvancedMeasurementNoPreview");
            ClearPreview(_samplesCanvas, "AdvancedMeasurementNoSamples");
            return;
        }

        AdvancedMeasurementEvaluation result = row.Evaluation;
        var lines = new List<string>
        {
            $"{UiText.Get("AdvancedMeasurementStatusLabel")}: {FormatSummary(row.Definition, result)}",
            UiText.Format("AdvancedMeasurementMethodRow", result.Method.Length > 0 ? UiText.Get(result.Method) : UiText.Get("AdvancedMeasurementStatusFailure"))
        };
        string[] sourceNames = row.Definition.Sources
            .Select(id => _sourceOptions.FirstOrDefault(option => option.Id == id)?.DisplayName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Cast<string>()
            .ToArray();
        lines.Add(UiText.Format("AdvancedMeasurementSourceLine", row.Definition.Name, string.Join(", ", sourceNames)));
        if (result.Metrics.Length > 0)
        {
            lines.AddRange(result.Metrics.Select(FormatMetric));
        }

        if (result.WithinLimits is bool withinLimits)
        {
            lines.Add(UiText.Get(withinLimits ? "AdvancedMeasurementStatusWithinLimits" : "AdvancedMeasurementStatusOutOfLimits"));
        }

        for (int i = 0; i < result.Samples.Length; i++)
        {
            double station = i < result.Stations.Length ? result.Stations[i] : i;
            lines.Add(UiText.Format("AdvancedMeasurementSampleTooltip", i + 1, Format(station), Format(result.Samples[i]), result.Metrics.FirstOrDefault()?.Unit ?? _getUnit()));
        }

        if (!result.Success && !string.IsNullOrWhiteSpace(result.Error))
        {
            lines.Add(UiText.Format("AdvancedMeasurementErrorRow", result.Error));
        }

        _detailsText.Text = string.Join(Environment.NewLine, lines);
        DrawWitness(_witnessCanvas, row);
        DrawSamples(_samplesCanvas, result.Stations, result.Samples);
    }

    private async void ExportCsvAsync()
    {
        var dialog = new SaveFileDialog
        {
            Title = UiText.Get("FileDialogSaveAnalysisCsvTitle"),
            Filter = UiText.Get("FileDialogCsvFilter"),
            DefaultExt = ".csv",
            AddExtension = true
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            await AdvancedMeasurementExportService.SaveAsync(
                dialog.FileName,
                _getRois(),
                _getPixelSize(),
                _getUnit(),
                _getCalibration(),
                _getBitmap?.Invoke(),
                _getImagePath?.Invoke(),
                _getWorkpiece?.Invoke());
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, $"{UiText.Get("AdvancedMeasurementSaveFailed")}: {exception.Message}", Title, MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private RoiBase? FindDefinitionOwner(IEnumerable<Guid> sourceIds)
    {
        var pending = new Queue<Guid>(sourceIds);
        var visited = new HashSet<Guid>();
        IReadOnlyList<AdvancedMeasurementDefinition> definitions = AdvancedMeasurementService.EnumerateDefinitions(_observedRois);
        while (pending.Count > 0)
        {
            Guid id = pending.Dequeue();
            if (!visited.Add(id))
            {
                continue;
            }

            RoiBase? roi = _observedRois.FirstOrDefault(item => item.MeasurementId == id);
            if (roi is not null)
            {
                return roi;
            }

            AdvancedMeasurementDefinition? definition = definitions.FirstOrDefault(item => item.Id == id);
            if (definition is not null)
            {
                foreach (Guid nestedSource in definition.Sources)
                {
                    pending.Enqueue(nestedSource);
                }
            }
        }

        return null;
    }

    private void QueueEvaluationRefresh()
    {
        if (_disposed || _refreshQueued || !IsLoaded)
        {
            return;
        }

        _refreshQueued = true;
        _ = Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            _refreshQueued = false;
            if (!_disposed)
            {
                RefreshSourceInventory();
                EvaluateAll();
            }
        }));
    }

    private void OnRoiPropertyChanged(object? sender, PropertyChangedEventArgs args) => QueueEvaluationRefresh();

    private void OnRoiCollectionChanged(object? sender, NotifyCollectionChangedEventArgs args) => QueueEvaluationRefresh();

    private void OnClosed(object? sender, EventArgs args)
    {
        _disposed = true;
        DetachRoiNotifications();
    }

    private void DetachRoiNotifications()
    {
        if (_observedCollection is not null)
        {
            _observedCollection.CollectionChanged -= OnRoiCollectionChanged;
            _observedCollection = null;
        }

        foreach (RoiBase roi in _observedRois)
        {
            roi.PropertyChanged -= OnRoiPropertyChanged;
        }

        foreach (INotifyCollectionChanged collection in _observedGeometryCollections)
        {
            collection.CollectionChanged -= OnRoiCollectionChanged;
        }

        _observedGeometryCollections.Clear();

        _observedRois = Array.Empty<RoiBase>();
    }

    private void UpdateParameterLabel()
    {
        _parameterLabel.Text = UiText.Get("AdvancedMeasurementParameterLabel");
        bool hasParameter = _operationCombo.SelectedValue is AdvancedMeasurementKind.GapProfile
            or AdvancedMeasurementKind.ProjectedDistance or AdvancedMeasurementKind.PeriodicPitch or AdvancedMeasurementKind.ContourDeviation;
        _parameterHintText.Visibility = hasParameter ? Visibility.Visible : Visibility.Collapsed;
        if (_parameterTextBox.Parent is UIElement parameterRow)
            parameterRow.Visibility = _parameterHintText.Visibility;
        bool isPosition = _operationCombo.SelectedValue is AdvancedMeasurementKind.Position;
        if (_nominalXTextBox.Parent is UIElement nominalRow)
            nominalRow.Visibility = isPosition ? Visibility.Visible : Visibility.Collapsed;
        _nominalInWorkpieceCheckBox.Visibility = isPosition ? Visibility.Visible : Visibility.Collapsed;
        if (_lowerLimitTextBox.Parent is UIElement limitRow)
            limitRow.Visibility = _operationCombo.SelectedValue is AdvancedMeasurementKind.Midpoint or AdvancedMeasurementKind.Intersection
                ? Visibility.Collapsed : Visibility.Visible;
    }

    private void UpdateSelectedSourceLabels()
    {
        foreach (SourceOption selected in _selectedSources.ToArray())
        {
            SourceOption? refreshed = _sourceOptions.FirstOrDefault(option => option.Id == selected.Id);
            if (refreshed is not null)
            {
                int index = _selectedSources.IndexOf(selected);
                _selectedSources[index] = refreshed;
            }
        }
    }

    private void DrawWitness(Canvas canvas, EvaluationRow row)
    {
        canvas.Children.Clear();
        var layers = new List<GeometryLayer>();
        foreach (Guid sourceId in row.Definition.Sources)
        {
            RoiBase? roi = _observedRois.FirstOrDefault(item => item.MeasurementId == sourceId);
            if (roi is null)
            {
                continue;
            }

            IReadOnlyList<PointD> sourcePoints = GetSourcePreviewPoints(roi);
            if (sourcePoints.Count > 0)
            {
                layers.Add(new GeometryLayer(sourcePoints.Select(point => ConvertSourcePoint(point, row.Definition)).ToArray(), Brushes.DimGray, roi is PolygonRoi { IsClosed: true } or CircleRoi or EllipseRoi or RotatedRect));
            }
        }

        foreach (PointD[] sourceContour in row.Evaluation.SourceContours)
        {
            if (sourceContour.Length > 0)
            {
                layers.Add(new GeometryLayer(
                    sourceContour.Select(point => ConvertPhysicalSourcePoint(point, row.Definition)).ToArray(),
                    Brushes.OrangeRed,
                    Closed: true));
            }
        }

        if (row.Definition.Kind == AdvancedMeasurementKind.GapProfile)
        {
            for (int i = 0; i + 1 < row.Evaluation.Geometry.Length; i += 2)
            {
                layers.Add(new GeometryLayer([row.Evaluation.Geometry[i], row.Evaluation.Geometry[i + 1]], Brushes.DodgerBlue, Closed: false));
            }
        }
        else if (row.Evaluation.Geometry.Length > 0)
        {
            bool closedGeometry = row.Definition.Kind is AdvancedMeasurementKind.Rectangle or AdvancedMeasurementKind.ShapeMetrics;
            layers.Add(new GeometryLayer(row.Evaluation.Geometry, Brushes.DodgerBlue, closedGeometry));
        }

        if (layers.Count == 0)
        {
            AddCanvasHint(canvas, UiText.Get("AdvancedMeasurementNoPreview"));
            return;
        }

        canvas.SizeChanged -= OnCanvasSizeChanged;
        canvas.SizeChanged += OnCanvasSizeChanged;
        DrawGeometryLayers(canvas, layers);
    }

    private void DrawSamples(Canvas canvas, IReadOnlyList<double> stations, IReadOnlyList<double> samples)
    {
        canvas.SizeChanged -= OnCanvasSizeChanged;
        canvas.SizeChanged += OnCanvasSizeChanged;
        canvas.Children.Clear();
        if (samples.Count == 0)
        {
            AddCanvasHint(canvas, UiText.Get("AdvancedMeasurementNoSamples"));
            return;
        }

        double[] xValues = stations.Count == samples.Count
            ? stations.ToArray()
            : Enumerable.Range(0, samples.Count).Select(index => (double)index).ToArray();
        double minX = xValues.Min();
        double maxX = xValues.Max();
        double minY = samples.Min();
        double maxY = samples.Max();
        var points = new PointD[samples.Count];
        for (int i = 0; i < samples.Count; i++)
        {
            points[i] = new PointD(xValues[i], samples[i]);
        }

        string unit = _evaluationRows.FirstOrDefault(row => ReferenceEquals(row.Evaluation.Samples, samples))?.Evaluation.Metrics.FirstOrDefault()?.Unit ?? _getUnit();
        DrawPolyline(canvas, points, Brushes.MediumSeaGreen, close: false, minX, maxX, minY, maxY,
            points.Select((point, index) => UiText.Format("AdvancedMeasurementSampleTooltip", index + 1, Format(point.X), Format(point.Y), unit)).ToArray());
        var stationLabel = new TextBlock
        {
            Text = stations.Count == samples.Count
                ? UiText.Format("AdvancedMeasurementSampleStationAxis", Format(minX), Format(maxX), _getUnit())
                : UiText.Format("AdvancedMeasurementSampleIndexAxis", Format(minX + 1), Format(maxX + 1)),
            Foreground = SystemColors.GrayTextBrush,
            FontSize = 10,
            IsHitTestVisible = false
        };
        Canvas.SetLeft(stationLabel, 4);
        Canvas.SetTop(stationLabel, 2);
        canvas.Children.Add(stationLabel);
        var valueLabel = new TextBlock
        {
            Text = UiText.Format("AdvancedMeasurementSampleValueAxis", Format(minY), Format(maxY), unit),
            Foreground = SystemColors.GrayTextBrush,
            FontSize = 10,
            HorizontalAlignment = HorizontalAlignment.Right,
            IsHitTestVisible = false
        };
        Canvas.SetLeft(valueLabel, 4);
        Canvas.SetTop(valueLabel, 16);
        canvas.Children.Add(valueLabel);
        if (samples.Count == 1)
        {
            AddCanvasHint(canvas, Format(samples[0]));
        }
    }

    private void DrawPolyline(Canvas canvas, IReadOnlyList<PointD> points, Brush stroke, bool close, double? minX = null, double? maxX = null, double? minY = null, double? maxY = null, IReadOnlyList<string>? markerTooltips = null)
    {
        double canvasWidth = Math.Max(canvas.ActualWidth, 120);
        double canvasHeight = Math.Max(canvas.ActualHeight, 100);
        double lowX = minX ?? points.Min(point => point.X);
        double highX = maxX ?? points.Max(point => point.X);
        double lowY = minY ?? points.Min(point => point.Y);
        double highY = maxY ?? points.Max(point => point.Y);
        // Profile axes have independent units/ranges; preserving geometry aspect would hide small deviations.
        double drawingWidth = canvasWidth - 32;
        double drawingHeight = canvasHeight - 64;
        Point Map(PointD point) => new(
            highX == lowX ? canvasWidth / 2 : 16 + (point.X - lowX) / (highX - lowX) * drawingWidth,
            highY == lowY ? 32 + drawingHeight / 2 : 32 + (highY - point.Y) / (highY - lowY) * drawingHeight);

        var polyline = new Polyline
        {
            Stroke = stroke,
            StrokeThickness = 2,
            StrokeLineJoin = PenLineJoin.Round,
            Points = new PointCollection(points.Select(Map))
        };
        if (close && points.Count > 2)
        {
            polyline.Points.Add(Map(points[0]));
        }

        canvas.Children.Add(polyline);
        for (int i = 0; i < points.Count; i++)
        {
            Point point = Map(points[i]);
            var marker = new Ellipse { Width = 5, Height = 5, Fill = stroke };
            if (markerTooltips is not null && i < markerTooltips.Count)
            {
                ToolTipService.SetToolTip(marker, markerTooltips[i]);
            }

            Canvas.SetLeft(marker, point.X - 2.5);
            Canvas.SetTop(marker, point.Y - 2.5);
            canvas.Children.Add(marker);
        }
    }

    private void OnCanvasSizeChanged(object sender, SizeChangedEventArgs args)
    {
        if (_resultsList.SelectedItem is EvaluationRow row)
        {
            if (ReferenceEquals(sender, _witnessCanvas))
            {
                DrawWitness(_witnessCanvas, row);
            }
            else if (ReferenceEquals(sender, _samplesCanvas))
            {
                DrawSamples(_samplesCanvas, row.Evaluation.Stations, row.Evaluation.Samples);
            }
        }
    }

    private PointD ConvertSourcePoint(PointD point, AdvancedMeasurementDefinition definition)
    {
        PointD corrected = _getCalibration()?.UndistortPoint(point) ?? point;
        var physical = new PointD(corrected.X * _getPixelSize(), corrected.Y * _getPixelSize());
        return ConvertPhysicalSourcePoint(physical, definition);
    }

    private PointD ConvertPhysicalSourcePoint(PointD physical, AdvancedMeasurementDefinition definition)
    {
        if (definition.Kind == AdvancedMeasurementKind.Position && definition.NominalInWorkpieceCoordinates && _getWorkpiece?.Invoke() is { IsDefined: true } workpiece)
        {
            PointD origin = ConvertImagePoint(workpiece.ToImage(new PointD(0, 0)));
            PointD axis = ConvertImagePoint(workpiece.ToImage(new PointD(1, 0)));
            physical = RigidTransform2D.FromDatum(origin, axis - origin).Transform(physical);
        }

        return physical;
    }

    private PointD ConvertImagePoint(PointD point)
    {
        PointD corrected = _getCalibration()?.UndistortPoint(point) ?? point;
        return new PointD(corrected.X * _getPixelSize(), corrected.Y * _getPixelSize());
    }

    private static IReadOnlyList<PointD> GetSourcePreviewPoints(RoiBase roi)
    {
        switch (roi)
        {
            case LineMeasureRoi line:
                return [line.P1, line.P2];
            case PolygonRoi { IsClosed: true } polygon:
                return polygon.Points.ToArray();
            case PolylineRoi polyline:
                return polyline.Points.ToArray();
            case RotatedRect rectangle:
                return RectangleCorners(rectangle.Center, rectangle.Width, rectangle.Height, rectangle.Angle);
            case EllipseRoi ellipse:
                return Enumerable.Range(0, 64).Select(index =>
                {
                    double angle = index * Math.Tau / 64;
                    double x = ellipse.RadiusX * Math.Cos(angle);
                    double y = ellipse.RadiusY * Math.Sin(angle);
                    double cos = Math.Cos(ellipse.Angle);
                    double sin = Math.Sin(ellipse.Angle);
                    return new PointD(ellipse.Center.X + (cos * x) - (sin * y), ellipse.Center.Y + (sin * x) + (cos * y));
                }).ToArray();
            case CircleRoi circle:
                return Enumerable.Range(0, 64).Select(index =>
                {
                    double angle = index * Math.Tau / 64;
                    return new PointD(circle.Center.X + (circle.Radius * Math.Cos(angle)), circle.Center.Y + (circle.Radius * Math.Sin(angle)));
                }).ToArray();
            default:
                return Array.Empty<PointD>();
        }
    }

    private static PointD[] RectangleCorners(PointD center, double width, double height, double angle)
    {
        double cos = Math.Cos(angle);
        double sin = Math.Sin(angle);
        double ux = cos * width / 2;
        double uy = sin * width / 2;
        double vx = -sin * height / 2;
        double vy = cos * height / 2;
        return
        [
            new(center.X - ux - vx, center.Y - uy - vy),
            new(center.X + ux - vx, center.Y + uy - vy),
            new(center.X + ux + vx, center.Y + uy + vy),
            new(center.X - ux + vx, center.Y - uy + vy)
        ];
    }

    private static void DrawGeometryLayers(Canvas canvas, IReadOnlyList<GeometryLayer> layers)
    {
        PointD[] points = layers.SelectMany(layer => layer.Points).ToArray();
        double canvasWidth = Math.Max(canvas.ActualWidth, 120);
        double canvasHeight = Math.Max(canvas.ActualHeight, 100);
        double lowX = points.Min(point => point.X);
        double highX = points.Max(point => point.X);
        double lowY = points.Min(point => point.Y);
        double highY = points.Max(point => point.Y);
        double scaleX = (canvasWidth - 24) / Math.Max(highX - lowX, 1e-9);
        double scaleY = (canvasHeight - 24) / Math.Max(highY - lowY, 1e-9);
        double scale = Math.Min(scaleX, scaleY);
        double drawingWidth = (highX - lowX) * scale;
        double drawingHeight = (highY - lowY) * scale;
        double left = (canvasWidth - drawingWidth) / 2;
        double top = (canvasHeight - drawingHeight) / 2;
        Point Map(PointD point) => new(left + ((point.X - lowX) * scale), canvasHeight - top - ((point.Y - lowY) * scale));

        foreach (GeometryLayer layer in layers)
        {
            if (layer.Points.Count == 0)
            {
                continue;
            }

            var polyline = new Polyline
            {
                Stroke = layer.Stroke,
                StrokeThickness = 2,
                StrokeLineJoin = PenLineJoin.Round,
                Points = new PointCollection(layer.Points.Select(Map))
            };
            if (layer.Closed && layer.Points.Count > 2)
            {
                polyline.Points.Add(Map(layer.Points[0]));
            }

            canvas.Children.Add(polyline);
        }
    }

    private static Canvas CreatePreviewCanvas() => new()
    {
        Background = SystemColors.WindowBrush,
        ClipToBounds = true,
        MinHeight = 140,
        Margin = new Thickness(0, 0, 6, 0)
    };

    private static void AddCanvasHint(Canvas canvas, string text)
    {
        var hint = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Foreground = SystemColors.GrayTextBrush, Margin = new Thickness(8) };
        canvas.Children.Add(hint);
    }

    private static void ClearPreview(Canvas canvas, string resourceKey)
    {
        canvas.Children.Clear();
        AddCanvasHint(canvas, UiText.Get(resourceKey));
    }

    private static TextBlock SectionHeader(string key) => new()
    {
        Text = UiText.Get(key),
        FontWeight = FontWeights.SemiBold,
        Margin = new Thickness(0, 8, 0, 4)
    };

    private static TextBox AddTextField(Panel panel, string labelKey)
    {
        panel.Children.Add(SectionHeader(labelKey));
        TextBox textBox = SetAutomationName(new TextBox(), UiText.Get(labelKey));
        panel.Children.Add(textBox);
        return textBox;
    }

    private static void AddCompactField(Grid grid, int labelColumn, int fieldColumn, string labelKey, out TextBox textBox)
    {
        var label = new TextBlock
        {
            Text = UiText.Get(labelKey),
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap
        };
        textBox = SetAutomationName(new TextBox(), UiText.Get(labelKey));
        Grid.SetColumn(label, labelColumn);
        Grid.SetColumn(textBox, fieldColumn);
        grid.Children.Add(label);
        grid.Children.Add(textBox);
    }

    private static Button CreateButton(string key, Action action, bool isDefault = false)
    {
        var button = new Button
        {
            Content = UiText.Get(key),
            IsDefault = isDefault,
            Margin = new Thickness(0, 0, 6, 4),
            Padding = new Thickness(8, 3, 8, 3)
        };
        AutomationProperties.SetName(button, UiText.Get(key));
        button.Click += (_, _) => action();
        return button;
    }

    private static T SetAutomationName<T>(T element, string name) where T : DependencyObject
    {
        AutomationProperties.SetName(element, name);
        return element;
    }

    private static string OperationResourceKey(AdvancedMeasurementKind kind) => kind switch
    {
        AdvancedMeasurementKind.Midpoint => "AdvancedMeasurementOperationMidpoint",
        AdvancedMeasurementKind.Intersection => "AdvancedMeasurementOperationIntersection",
        AdvancedMeasurementKind.Projection => "AdvancedMeasurementOperationProjection",
        AdvancedMeasurementKind.Centerline => "AdvancedMeasurementOperationCenterline",
        AdvancedMeasurementKind.AngleBisector => "AdvancedMeasurementOperationAngleBisector",
        AdvancedMeasurementKind.ShortestDistance => "AdvancedMeasurementOperationShortestDistance",
        AdvancedMeasurementKind.ProjectedDistance => "AdvancedMeasurementOperationProjectedDistance",
        AdvancedMeasurementKind.Straightness => "AdvancedMeasurementOperationStraightness",
        AdvancedMeasurementKind.Circularity => "AdvancedMeasurementOperationCircularity",
        AdvancedMeasurementKind.Rectangle => "AdvancedMeasurementOperationRectangle",
        AdvancedMeasurementKind.Slot => "AdvancedMeasurementOperationSlot",
        AdvancedMeasurementKind.GapProfile => "AdvancedMeasurementOperationGapProfile",
        AdvancedMeasurementKind.Position => "AdvancedMeasurementOperationPosition",
        AdvancedMeasurementKind.Pitch => "AdvancedMeasurementOperationPitch",
        AdvancedMeasurementKind.DistributionCircle => "AdvancedMeasurementOperationDistributionCircle",
        AdvancedMeasurementKind.ContourDeviation => "AdvancedMeasurementOperationContourDeviation",
        AdvancedMeasurementKind.ShapeMetrics => "AdvancedMeasurementOperationShapeMetrics",
        AdvancedMeasurementKind.PeriodicPitch => "AdvancedMeasurementOperationPeriodicPitch",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
    };

    private void ShowWarning(string resourceKey) => MessageBox.Show(this, UiText.Get(resourceKey), Title, MessageBoxButton.OK, MessageBoxImage.Warning);

    private static bool TryParseDouble(string text, out double value)
    {
        return double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value) && double.IsFinite(value);
    }

    private static bool TryParseOptionalDouble(string text, out double? value)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            value = null;
            return true;
        }

        if (TryParseDouble(text, out double parsed))
        {
            value = parsed;
            return true;
        }

        value = null;
        return false;
    }

    private static string Format(double value) => value.ToString("G6", CultureInfo.CurrentCulture);

    private sealed record SourceOption(Guid Id, string DisplayName, RoiBase? Roi, AdvancedMeasurementDefinition? Definition);

    private sealed record OperationOption(AdvancedMeasurementKind Kind, string DisplayName);

    private sealed record GeometryLayer(IReadOnlyList<PointD> Points, Brush Stroke, bool Closed);

    private sealed record EvaluationRow(AdvancedMeasurementDefinition Definition, AdvancedMeasurementEvaluation Evaluation, string Summary);
}
