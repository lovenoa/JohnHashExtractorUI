using System.Diagnostics;
using System.Text;
using JohnHashExtractor.Models;
using JohnHashExtractor.Services;

namespace JohnHashExtractor.Ui;

public sealed class MainForm : Form
{
    private readonly AppConfigStore _configStore;
    private readonly ConverterScanner _scanner;
    private readonly ExtractionService _extractionService;
    private readonly AppConfig _config;

    private readonly TextBox _johnPath = new() { Dock = DockStyle.Fill, ReadOnly = true };
    private readonly Label _johnStatus = new() { AutoSize = true, ForeColor = Color.DimGray };
    private readonly TextBox _filePath = new() { Dock = DockStyle.Fill, ReadOnly = true };
    private readonly Label _fileInfo = new() { AutoSize = true, ForeColor = Color.DimGray };
    private readonly Label _typeInfo = new() { AutoSize = true, ForeColor = Color.DimGray };
    private readonly ComboBox _converterCombo = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
    private readonly Label _converterInfo = new() { AutoSize = true, ForeColor = Color.DimGray };
    private readonly Button _addCustomConverter = new() { Text = "添加自定义转换器", AutoSize = true };
    private readonly Button _removeCustomConverter = new() { Text = "移除自定义转换器", AutoSize = true, Enabled = false };
    private readonly TextBox _outputDirectory = new() { Dock = DockStyle.Fill };
    private readonly Button _browseOutputDirectory = new() { Text = "选择目录", AutoSize = true };
    private readonly ComboBox _outputFormatCombo = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 360 };
    private readonly Label _outputPreview = new() { AutoSize = true, ForeColor = Color.DimGray, MaximumSize = new Size(860, 0) };
    private readonly Button _extractButton = new() { Text = "提取 Hash", AutoSize = true };
    private readonly Button _copyButton = new() { Text = "复制 Hash", AutoSize = true };
    private readonly Button _saveButton = new() { Text = "保存 Hash", AutoSize = true };
    private readonly Button _openFolderButton = new() { Text = "打开输出目录", AutoSize = true };
    private readonly Button _reextractButton = new() { Text = "重新提取", AutoSize = true };
    private readonly Button _clearButton = new() { Text = "清除结果", AutoSize = true };
    private readonly RichTextBox _output = new()
    {
        Dock = DockStyle.Fill,
        ReadOnly = true,
        WordWrap = false,
        Font = new Font("Consolas", 9.5F),
        DetectUrls = false
    };

    private ConverterScanResult? _scan;
    private string? _inputPath;
    private ExtractionResult? _lastResult;
    private CancellationTokenSource? _extractionCancellation;

    public MainForm(
        AppConfigStore configStore,
        ConverterScanner scanner,
        ExtractionService extractionService,
        AppConfig config)
    {
        _configStore = configStore;
        _scanner = scanner;
        _extractionService = extractionService;
        _config = config;

        _outputDirectory.Text = config.OutputDirectory ?? string.Empty;
        _outputFormatCombo.Items.Add("John 原始格式 (*.hash)");
        _outputFormatCombo.Items.Add("Hashcat 直接使用 (*.hashcat.hash)");
        _outputFormatCombo.SelectedIndex = config.OutputFileFormat == OutputFileFormat.Hashcat ? 1 : 0;

        Text = "John Hash Extractor";
        MinimumSize = new Size(1000, 720);
        Size = new Size(1200, 850);
        StartPosition = FormStartPosition.CenterScreen;
        AllowDrop = true;
        BackColor = Color.White;

        BuildLayout();
        WireEvents();
    }

    private void BuildLayout()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 9,
            Padding = new Padding(16),
            AutoScroll = true
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        root.Controls.Add(CreateSection("John run 目录", _johnPath, _johnStatus), 0, 0);
        root.Controls.Add(CreateSection("输入文件", _filePath, _fileInfo), 0, 1);
        root.Controls.Add(CreateSection("文件类型", _typeInfo), 0, 2);
        var converterTools = new FlowLayoutPanel
        {
            AutoSize = true,
            WrapContents = false,
            FlowDirection = FlowDirection.LeftToRight,
            Margin = new Padding(0, 4, 0, 0)
        };
        converterTools.Controls.Add(_addCustomConverter);
        converterTools.Controls.Add(_removeCustomConverter);
        root.Controls.Add(CreateSection("转换器", _converterCombo, _converterInfo, converterTools), 0, 3);

        var actions = new FlowLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            WrapContents = false,
            FlowDirection = FlowDirection.LeftToRight,
            Padding = new Padding(0, 8, 0, 8)
        };
        actions.Controls.Add(_extractButton);
        actions.Controls.Add(_reextractButton);
        actions.Controls.Add(_copyButton);
        actions.Controls.Add(_saveButton);
        actions.Controls.Add(_openFolderButton);
        actions.Controls.Add(_clearButton);
        root.Controls.Add(actions, 0, 4);

        root.Controls.Add(CreateOutputSettingsSection(), 0, 5);
        root.Controls.Add(CreateOutputSection("转换器输出", _output), 0, 7);

        var footer = new Label
        {
            AutoSize = true,
            ForeColor = Color.Gray,
            Text = "Hashcat 格式仅对已确认可安全映射的 Hash 生成，不支持的格式会明确报错。"
        };
        root.Controls.Add(footer, 0, 8);

        Controls.Add(root);
    }

    private static Control CreateSection(string title, Control content, params Control[] extras)
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 1,
            RowCount = 2 + extras.Length,
            Padding = new Padding(0, 4, 0, 4)
        };
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        for (var i = 0; i < extras.Length; i++)
        {
            panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        }

        var heading = new Label
        {
            Text = title,
            Font = new Font("Segoe UI", 9F, FontStyle.Bold),
            ForeColor = Color.FromArgb(55, 55, 55),
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 3)
        };
        panel.Controls.Add(heading, 0, 0);
        panel.Controls.Add(content, 0, 1);
        for (var i = 0; i < extras.Length; i++)
        {
            panel.Controls.Add(extras[i], 0, i + 2);
        }

        return panel;
    }

    private static Control CreateOutputSection(string title, Control content)
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(0, 4, 0, 4)
        };
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        panel.Controls.Add(new Label
        {
            Text = title,
            Font = new Font("Segoe UI", 9F, FontStyle.Bold),
            ForeColor = Color.FromArgb(55, 55, 55),
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 3)
        }, 0, 0);
        content.Dock = DockStyle.Fill;
        panel.Controls.Add(content, 0, 1);
        return panel;
    }

    private Control CreateOutputSettingsSection()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(0, 4, 0, 4)
        };
        for (var i = 0; i < 4; i++)
        {
            panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        }

        panel.Controls.Add(new Label
        {
            Text = "输出设置",
            Font = new Font("Segoe UI", 9F, FontStyle.Bold),
            ForeColor = Color.FromArgb(55, 55, 55),
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 3)
        }, 0, 0);

        var directoryRow = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            ColumnCount = 3,
            RowCount = 1
        };
        directoryRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        directoryRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        directoryRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        directoryRow.Controls.Add(new Label { Text = "目录", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0);
        directoryRow.Controls.Add(_outputDirectory, 1, 0);
        directoryRow.Controls.Add(_browseOutputDirectory, 2, 0);
        panel.Controls.Add(directoryRow, 0, 1);

        var formatRow = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            ColumnCount = 2,
            RowCount = 1
        };
        formatRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        formatRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        formatRow.Controls.Add(new Label { Text = "格式", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0);
        formatRow.Controls.Add(_outputFormatCombo, 1, 0);
        panel.Controls.Add(formatRow, 0, 2);
        panel.Controls.Add(_outputPreview, 0, 3);
        return panel;
    }

    private void WireEvents()
    {
        Load += async (_, _) => await OnLoadedAsync();
        _extractButton.Click += async (_, _) => await ExtractAsync();
        _reextractButton.Click += async (_, _) => await ExtractAsync();
        _copyButton.Click += (_, _) => CopyHash();
        _saveButton.Click += async (_, _) => await SaveHashAsync();
        _openFolderButton.Click += (_, _) => OpenOutputDirectory();
        _clearButton.Click += (_, _) => ClearResult();
        _converterCombo.SelectedIndexChanged += (_, _) => UpdateConverterInfo();
        _browseOutputDirectory.Click += (_, _) => ChooseOutputDirectory();
        _addCustomConverter.Click += (_, _) => AddCustomConverter();
        _removeCustomConverter.Click += (_, _) => RemoveCustomConverter();
        _outputFormatCombo.SelectedIndexChanged += (_, _) =>
        {
            UpdateOutputPreview();
            SaveOutputSelections();
        };
        _outputDirectory.TextChanged += (_, _) => UpdateOutputPreview();
        _outputDirectory.Leave += (_, _) => SaveOutputSelections();

        DragEnter += (_, e) =>
        {
            if (e.Data?.GetDataPresent(DataFormats.FileDrop) == true)
            {
                e.Effect = DragDropEffects.Copy;
            }
        };
        DragDrop += (_, e) =>
        {
            if (e.Data?.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
            {
                SetInputFile(files[0]);
            }
        };
    }

    private async Task OnLoadedAsync()
    {
        _johnPath.Text = _config.JohnRunDirectory ?? string.Empty;
        UpdateOutputPreview();
        if (Directory.Exists(_config.JohnRunDirectory))
        {
            await RefreshJohnAsync(_config.JohnRunDirectory!);
            return;
        }

        BeginInvoke(async () => await ChooseJohnDirectoryAsync());
    }

    private void ChooseOutputDirectory()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "选择 Hash 输出目录",
            UseDescriptionForTitle = true,
            SelectedPath = _outputDirectory.Text
        };

        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            _outputDirectory.Text = dialog.SelectedPath;
            SaveConfig();
        }
    }

    private OutputFileFormat GetSelectedOutputFormat()
    {
        return _outputFormatCombo.SelectedIndex == 1 ? OutputFileFormat.Hashcat : OutputFileFormat.John;
    }

    private void AddCustomConverter()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "添加自定义转换器",
            CheckFileExists = true,
            Multiselect = false,
            Filter = "转换器 (*.exe;*.py;*.pl;*.js;*.lua)|*.exe;*.py;*.pl;*.js;*.lua|所有文件 (*.*)|*.*"
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        var path = Path.GetFullPath(dialog.FileName);
        if (!_config.CustomConverterPaths.Contains(path, StringComparer.OrdinalIgnoreCase))
        {
            _config.CustomConverterPaths.Add(path);
        }

        SaveConfig();
        RefreshCustomConverters(selectPath: path);
    }

    private void RemoveCustomConverter()
    {
        if (GetSelectedCandidate() is not { IsCustom: true } selected)
        {
            return;
        }

        var answer = MessageBox.Show(
            this,
            "仅从 John Hash Extractor 中移除此自定义转换器，不会删除程序文件。是否继续？",
            "移除自定义转换器",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question);
        if (answer != DialogResult.Yes)
        {
            return;
        }

        _config.CustomConverterPaths.RemoveAll(path => string.Equals(path, selected.FilePath, StringComparison.OrdinalIgnoreCase));
        SaveConfig();
        RefreshCustomConverters(selectPath: null);
    }

    private void RefreshCustomConverters(string? selectPath)
    {
        if (_scan is null)
        {
            return;
        }

        var nonCustom = _scan.Candidates.Where(candidate => !candidate.IsCustom).ToArray();
        var custom = _config.CustomConverterPaths
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(_scanner.ProbeCustom)
            .ToArray();
        _scan = new ConverterScanResult
        {
            RunDirectory = _scan.RunDirectory,
            Candidates = nonCustom.Concat(custom).ToArray(),
            Environment = _scan.Environment
        };

        RebuildConverterList();
        if (selectPath is not null)
        {
            var selected = _converterCombo.Items.OfType<ConverterCandidate>()
                .FirstOrDefault(candidate => string.Equals(candidate.FilePath, selectPath, StringComparison.OrdinalIgnoreCase));
            if (selected is not null)
            {
                _converterCombo.SelectedItem = selected;
            }
        }
        UpdateConverterInfo();
    }

    private void UpdateOutputPreview()
    {
        if (_inputPath is null)
        {
            _outputPreview.Text = "选择输入文件后显示实际输出路径。";
            return;
        }

        var outputPath = OutputNaming.GetOutputPath(_inputPath, _outputDirectory.Text, GetSelectedOutputFormat());
        var rawOnly = GetSelectedCandidate()?.IsCustom == true;
        var outputFormat = rawOnly ? OutputFileFormat.John : GetSelectedOutputFormat();
        outputPath = OutputNaming.GetOutputPath(_inputPath, _outputDirectory.Text, outputFormat);
        _outputPreview.Text = rawOnly
            ? "输出文件：" + outputPath + "（自定义转换器原样输出）"
            : "输出文件：" + outputPath;
    }

    private void SaveOutputSelections()
    {
        _config.OutputDirectory = _outputDirectory.Text;
        _config.OutputFileFormat = GetSelectedOutputFormat();
        SaveConfig();
    }

    private bool TrySyncOutputSettings()
    {
        if (_inputPath is null)
        {
            return false;
        }

        try
        {
            _config.OutputDirectory = string.IsNullOrWhiteSpace(_outputDirectory.Text)
                ? Path.GetDirectoryName(_inputPath)
                : Path.GetFullPath(_outputDirectory.Text);
            _config.OutputFileFormat = GetSelectedOutputFormat();
            SaveConfig();
            UpdateOutputPreview();
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            MessageBox.Show(this, "输出目录无效：" + ex.Message, "输出设置", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }
    }

    private async Task ChooseJohnDirectoryAsync()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "选择 John the Ripper 的 run 目录",
            UseDescriptionForTitle = true
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            _johnStatus.Text = "未设置 John run 目录。";
            return;
        }

        _config.JohnRunDirectory = dialog.SelectedPath;
        _johnPath.Text = dialog.SelectedPath;
        SaveConfig();
        await RefreshJohnAsync(dialog.SelectedPath);
    }

    private async Task RefreshJohnAsync(string runDirectory)
    {
        _johnStatus.Text = "正在扫描转换器和运行环境...";
        SetBusy(true);
        try
        {
            var discovered = await Task.Run(() => _scanner.Scan(runDirectory));
            var customCandidates = _config.CustomConverterPaths
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(_scanner.ProbeCustom)
                .ToArray();
            _scan = new ConverterScanResult
            {
                RunDirectory = discovered.RunDirectory,
                Candidates = discovered.Candidates.Concat(customCandidates).ToArray(),
                Environment = discovered.Environment
            };
            var available = _scan.Candidates.Count(candidate => candidate.Availability == ConverterAvailability.Available);
            _johnStatus.Text = $"John: OK | 转换器: {_scan.Candidates.Count} | 依赖可用: {available}";
            RebuildConverterList();
            UpdateConverterInfo();
        }
        catch (Exception ex)
        {
            _scan = null;
            _johnStatus.Text = "扫描失败：" + ex.Message;
            _converterCombo.Items.Clear();
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void RebuildConverterList()
    {
        _converterCombo.Items.Clear();
        _converterCombo.Items.Add("自动选择（依赖优先）");
        if (_scan is null)
        {
            _converterCombo.SelectedIndex = 0;
            return;
        }

        foreach (var candidate in _scan.Candidates)
        {
            _converterCombo.Items.Add(candidate);
        }

        _converterCombo.SelectedIndex = 0;
    }

    private void SetInputFile(string path)
    {
        if (!File.Exists(path))
        {
            MessageBox.Show(this, "文件不存在。", "John Hash Extractor", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        _inputPath = Path.GetFullPath(path);
        _filePath.Text = _inputPath;
        var info = new FileInfo(_inputPath);
        _fileInfo.Text = $"{info.Length:N0} bytes | 修改时间：{info.LastWriteTime:yyyy-MM-dd HH:mm:ss}";
        var type = FileTypeDetector.Detect(_inputPath);
        _typeInfo.Text = $"{type.DisplayName} | 扩展名：{type.Extension}";
        if (string.IsNullOrWhiteSpace(_outputDirectory.Text))
        {
            _outputDirectory.Text = Path.GetDirectoryName(_inputPath) ?? string.Empty;
        }
        UpdateOutputPreview();
        UpdateConverterInfo();
    }

    private void UpdateConverterInfo()
    {
        if (_scan is null || _inputPath is null)
        {
            _converterInfo.Text = string.Empty;
            return;
        }

        if (_converterCombo.SelectedItem is ConverterCandidate candidate)
        {
            _converterInfo.Text = $"{candidate.DependencyMessage} | {candidate.FilePath}";
            _removeCustomConverter.Enabled = candidate.IsCustom;
            _outputFormatCombo.Enabled = !candidate.IsCustom;
            UpdateOutputPreview();
            return;
        }

        var type = FileTypeDetector.Detect(_inputPath);
        var matching = _scan.Candidates
            .Where(item => !item.IsCustom)
            .Where(item => type.ConverterFamilies.Contains(item.Family, StringComparer.OrdinalIgnoreCase))
            .ToArray();
        var preferred = matching
            .Where(item => item.Availability == ConverterAvailability.Available)
            .OrderByDescending(item => item.Runtime == ConverterRuntime.Native)
            .FirstOrDefault();
        _converterInfo.Text = preferred is null
            ? "没有确认可用的默认转换器，可在上方手动选择。"
            : $"自动候选：{preferred.Name} | {preferred.DependencyMessage}";
        _removeCustomConverter.Enabled = false;
        _outputFormatCombo.Enabled = true;
        UpdateOutputPreview();
    }

    private ConverterCandidate? GetSelectedCandidate()
    {
        return _converterCombo.SelectedItem as ConverterCandidate;
    }

    private async Task ExtractAsync()
    {
        if (_inputPath is null)
        {
            MessageBox.Show(this, "请先选择输入文件。", "John Hash Extractor", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (!Directory.Exists(_config.JohnRunDirectory))
        {
            await ChooseJohnDirectoryAsync();
            if (!Directory.Exists(_config.JohnRunDirectory))
            {
                return;
            }
        }

        if (_scan is null)
        {
            await RefreshJohnAsync(_config.JohnRunDirectory!);
            if (_scan is null)
            {
                return;
            }
        }

        if (!TrySyncOutputSettings())
        {
            return;
        }

        _extractionCancellation?.Cancel();
        _extractionCancellation = new CancellationTokenSource();
        SetBusy(true);
        _output.Text = "正在执行转换器...";
        try
        {
            var result = await _extractionService.ExtractAsync(
                _inputPath,
                GetSelectedCandidate(),
                _config,
                _scan,
                _extractionCancellation.Token);
            _lastResult = result;
            DisplayResult(result);

            if (result.Success)
            {
                await SaveResultWithPromptAsync(result);
            }
        }
        catch (Exception ex)
        {
            _lastResult = null;
            _output.Text = "提取失败。" + Environment.NewLine + ex.Message;
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void DisplayResult(ExtractionResult result)
    {
        var builder = new StringBuilder();
        builder.AppendLine(result.Success ? "转换成功" : "转换失败");
        builder.AppendLine($"转换器: {result.ConverterName}");
        builder.AppendLine($"命令: {result.Command}");
        builder.AppendLine($"返回码: {result.ReturnCode}");
        builder.AppendLine($"转换器输出格式: {result.OutputFormat}");
        builder.AppendLine(result.RawOutputOnly
            ? "保存格式: 自定义转换器原样输出"
            : $"保存格式: {(GetSelectedOutputFormat() == OutputFileFormat.Hashcat ? "Hashcat 直接使用" : "John 原始格式")}");
        builder.AppendLine($"输出文件: {result.OutputPath}");
        if (!string.IsNullOrWhiteSpace(result.DependencyMessage))
        {
            builder.AppendLine($"依赖状态: {result.DependencyMessage}");
        }

        if (!string.IsNullOrWhiteSpace(result.FailureReason))
        {
            builder.AppendLine($"失败原因: {result.FailureReason}");
        }

        builder.AppendLine();
        builder.AppendLine("--- stdout ---");
        builder.AppendLine(result.StandardOutput);
        if (!string.IsNullOrWhiteSpace(result.StandardError))
        {
            builder.AppendLine();
            builder.AppendLine("--- stderr ---");
            builder.AppendLine(result.StandardError);
        }

        _output.Text = builder.ToString();
    }

    private async Task SaveResultWithPromptAsync(ExtractionResult result)
    {
        if (_inputPath is null)
        {
            return;
        }

        var outputFormat = result.RawOutputOnly ? OutputFileFormat.John : GetSelectedOutputFormat();
        var destination = OutputNaming.GetOutputPath(_inputPath, _config.OutputDirectory, outputFormat);
        result.OutputPath = destination;
        if (File.Exists(destination))
        {
            var answer = MessageBox.Show(
                this,
                $"输出文件已存在：{destination}{Environment.NewLine}是否覆盖？",
                "保存 Hash",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);
            if (answer != DialogResult.Yes)
            {
                return;
            }
        }

        await SaveResultAsync(result, destination, outputFormat, overwrite: true);
    }

    private async Task SaveHashAsync()
    {
        if (_lastResult is null || !_lastResult.Success)
        {
            MessageBox.Show(this, "当前没有可保存的成功结果。", "John Hash Extractor", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (_inputPath is null || !TrySyncOutputSettings())
        {
            return;
        }

        var outputFormat = _lastResult.RawOutputOnly ? OutputFileFormat.John : GetSelectedOutputFormat();
        var outputPath = OutputNaming.GetOutputPath(_inputPath, _config.OutputDirectory, outputFormat);
        var initialDirectory = Path.GetDirectoryName(outputPath);
        using var dialog = new SaveFileDialog
        {
            Title = "保存 Hash",
            InitialDirectory = Directory.Exists(initialDirectory) ? initialDirectory : string.Empty,
            FileName = Path.GetFileName(outputPath),
            DefaultExt = outputFormat == OutputFileFormat.Hashcat ? "hashcat.hash" : "hash",
            Filter = outputFormat == OutputFileFormat.Hashcat
                ? "Hashcat hash files (*.hashcat.hash)|*.hashcat.hash|All files (*.*)|*.*"
                : _lastResult.RawOutputOnly
                    ? "原始输出文件 (*.hash)|*.hash|All files (*.*)|*.*"
                    : "John hash files (*.hash)|*.hash|All files (*.*)|*.*"
        };
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            await SaveResultAsync(_lastResult, dialog.FileName, outputFormat, overwrite: true);
        }
    }

    private async Task SaveResultAsync(
        ExtractionResult result,
        string destination,
        OutputFileFormat outputFormat,
        bool overwrite)
    {
        try
        {
            await _extractionService.SaveAsync(result, destination, outputFormat, overwrite);
            result.OutputPath = destination;
            _output.AppendText(Environment.NewLine + $"已保存：{destination}");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "保存失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void CopyHash()
    {
        if (_lastResult is null || !_lastResult.Success)
        {
            MessageBox.Show(this, "当前没有可复制的成功结果。", "John Hash Extractor", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        try
        {
            var content = _lastResult.RawOutputOnly
                ? _lastResult.StandardOutput
                : OutputFormatter.Format(
                    _lastResult.StandardOutput,
                    GetSelectedOutputFormat(),
                    _lastResult.OutputFormat);
            Clipboard.SetText(content);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "复制失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void OpenOutputDirectory()
    {
        var directory = _outputDirectory.Text;
        if (string.IsNullOrWhiteSpace(directory))
        {
            directory = _inputPath is null ? string.Empty : Path.GetDirectoryName(_inputPath);
        }

        if (!string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory))
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = directory,
                UseShellExecute = true
            });
        }
    }

    private void ClearResult()
    {
        _lastResult = null;
        _output.Clear();
    }

    private void SetBusy(bool busy)
    {
        _extractButton.Enabled = !busy;
        _reextractButton.Enabled = !busy && _inputPath is not null;
        _copyButton.Enabled = !busy && _lastResult?.Success == true;
        _saveButton.Enabled = !busy && _lastResult?.Success == true;
        _openFolderButton.Enabled = !busy && _inputPath is not null;
        _clearButton.Enabled = !busy && _output.TextLength > 0;
        UseWaitCursor = busy;
    }

    private void SaveConfig()
    {
        try
        {
            _configStore.Save(_config);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "配置保存失败：" + ex.Message, "John Hash Extractor", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }
}
