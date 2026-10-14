using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Data;
using System.Windows.Input;
using Autodesk.Revit.UI;
using ClashNavigator.Core;
using ClashNavigator.Models;
using ClashNavigator.Services;

namespace ClashNavigator.ViewModels
{
    // Drives the whole window. It knows nothing about WPF controls and nothing about how to talk
    // to Revit - it parses, filters, and hands jobs to RevitTask.
    public class MainViewModel : ViewModelBase
    {
        private const string ReportFilter = "Navisworks clash report (*.xml)|*.xml|All files (*.*)|*.*";
        private const string AllFilter = "All";

        // The five statuses Navisworks Clash Detective uses.
        private static readonly string[] CanonicalStatuses =
            { "New", "Active", "Reviewed", "Approved", "Resolved" };

        // A starting point for the Assigned to column. The user can type anything else and it
        // joins the list for the rest of the session.
        private static readonly string[] DefaultAssignees =
            { "MEP contractor", "HVAC", "Plumbing", "Electrical", "Structure", "Architecture", "Design team" };

        private readonly IFileDialogService _dialogs;
        private readonly ClashReportParser _parser = new ClashReportParser();
        private List<ClashItem> _items = new List<ClashItem>();
        private bool _groupDuplicates;

        private string _reportPath = string.Empty;
        private string _searchText = string.Empty;
        private string _selectedTest = AllFilter;
        private string _selectedStatus = AllFilter;
        private string _statusMessage = "Load a Navisworks clash report (.xml) to start.";
        private ClashRowViewModel _selectedClash;

        private bool _showOnSelect = true;
        private bool _isolateOnShow;
        private bool _zoomOnShow = true;
        private double _paddingMeters = 0.5;

        public MainViewModel(IFileDialogService dialogs)
        {
            _dialogs = dialogs;

            Clashes = new ObservableCollection<ClashRowViewModel>();
            ClashesView = CollectionViewSource.GetDefaultView(Clashes);
            ClashesView.Filter = PassesFilter;

            TestOptions = new ObservableCollection<string> { AllFilter };
            StatusOptions = new ObservableCollection<string> { AllFilter };
            foreach (string status in CanonicalStatuses) StatusOptions.Add(status);

            AssigneeValues = new ObservableCollection<string>(DefaultAssignees);

            BrowseCommand = new RelayCommand(Browse);
            ShowCommand = new RelayCommand(ShowSelectedClash, () => SelectedClash != null);
            IsolateCommand = new RelayCommand(IsolateSelectedClash, () => SelectedClash != null);
            ResetViewCommand = new RelayCommand(ResetView);
            CopyDetailsCommand = new RelayCommand(CopyDetails, () => SelectedClash != null);
            CopyIdACommand = new RelayCommand(() => CopyId(true), () => SelectedClash != null);
            CopyIdBCommand = new RelayCommand(() => CopyId(false), () => SelectedClash != null);
            ApplyToDuplicatesCommand = new RelayCommand(ApplyToDuplicates, () => SelectedClash != null);
            ExportCommand = new RelayCommand(ExportReport, () => Clashes.Count > 0);

            // Anything that blows up inside Revit lands in the status bar instead of a popup.
            RevitTask.SetErrorReporter(error => StatusMessage = "Revit reported: " + error);
        }

        // ---------- data ----------

        public ObservableCollection<ClashRowViewModel> Clashes { get; private set; }
        public ICollectionView ClashesView { get; private set; }
        public ObservableCollection<string> TestOptions { get; private set; }
        public ObservableCollection<string> StatusOptions { get; private set; }

        // Bound to the status dropdown inside each grid row.
        public string[] StatusValues
        {
            get { return CanonicalStatuses; }
        }

        // Bound to the editable Assigned to dropdown inside each grid row. Free typing is allowed,
        // and anything typed is added here so the next row can pick it instead of retyping it -
        // which is what keeps "MEP contractor" from becoming four different spellings.
        public ObservableCollection<string> AssigneeValues { get; private set; }

        // ---------- bound state ----------

        public string ReportPath
        {
            get { return _reportPath; }
            set { SetProperty(ref _reportPath, value); }
        }

        public string SearchText
        {
            get { return _searchText; }
            set { if (SetProperty(ref _searchText, value)) RefreshFilter(); }
        }

        public string SelectedTest
        {
            get { return _selectedTest; }
            set { if (SetProperty(ref _selectedTest, value)) RefreshFilter(); }
        }

        public string SelectedStatus
        {
            get { return _selectedStatus; }
            set { if (SetProperty(ref _selectedStatus, value)) RefreshFilter(); }
        }

        public ClashRowViewModel SelectedClash
        {
            get { return _selectedClash; }
            set
            {
                if (!SetProperty(ref _selectedClash, value)) return;

                // This is the behaviour the tool exists for: click a clash, see it in the model.
                if (_showOnSelect && _selectedClash != null) ShowSelectedClash();
            }
        }

        public bool ShowOnSelect
        {
            get { return _showOnSelect; }
            set { SetProperty(ref _showOnSelect, value); }
        }

        // Collapses the list into one heading per pair of element types, with a count.
        // A 272 row report is usually a much smaller number of actual coordination issues.
        public bool GroupDuplicates
        {
            get { return _groupDuplicates; }
            set { if (SetProperty(ref _groupDuplicates, value)) ApplyGrouping(); }
        }

        public bool IsolateOnShow
        {
            get { return _isolateOnShow; }
            set { SetProperty(ref _isolateOnShow, value); }
        }

        public bool ZoomOnShow
        {
            get { return _zoomOnShow; }
            set { SetProperty(ref _zoomOnShow, value); }
        }

        public double PaddingMeters
        {
            get { return _paddingMeters; }
            set { SetProperty(ref _paddingMeters, value); }
        }

        public string StatusMessage
        {
            get { return _statusMessage; }
            set { SetProperty(ref _statusMessage, value); }
        }

        public string CountSummary
        {
            get
            {
                int visible = ClashesView == null ? 0 : ClashesView.Cast<object>().Count();
                return visible + " of " + Clashes.Count + " clashes";
            }
        }

        // ---------- commands ----------

        public ICommand BrowseCommand { get; private set; }
        public ICommand ShowCommand { get; private set; }
        public ICommand IsolateCommand { get; private set; }
        public ICommand ResetViewCommand { get; private set; }
        public ICommand CopyDetailsCommand { get; private set; }
        public ICommand CopyIdACommand { get; private set; }
        public ICommand CopyIdBCommand { get; private set; }
        public ICommand ApplyToDuplicatesCommand { get; private set; }
        public ICommand ExportCommand { get; private set; }

        // Picking a file is the whole gesture - it loads and checks the report immediately.
        private void Browse()
        {
            string path = _dialogs.AskForOpenPath("Select the Navisworks clash report", ReportFilter);
            if (string.IsNullOrEmpty(path)) return;

            ReportPath = path;
            LoadReport();
        }

        private void LoadReport()
        {
            if (!File.Exists(ReportPath))
            {
                StatusMessage = "That file does not exist: " + ReportPath;
                return;
            }

            try
            {
                _items = _parser.Parse(ReportPath);

                // Anything the coordinator marked in an earlier session comes back before the
                // rows are built, so the list opens exactly where they left it.
                int restored = ClashStatusStore.Load(ReportPath, _items);

                Clashes.Clear();
                foreach (ClashItem item in _items)
                {
                    var row = new ClashRowViewModel(item);
                    row.PropertyChanged += OnRowPropertyChanged;
                    Clashes.Add(row);
                }

                RebuildFilterOptions();
                ApplyGrouping();
                RefreshFilter();

                StatusMessage = "Loaded " + _items.Count + " clashes (units: " + _parser.ReportUnits + ").";

                if (restored > 0)
                    StatusMessage += " " + restored + " status" + (restored == 1 ? "" : "es") +
                                     " restored from your last session.";

                if (_items.Count == 0)
                {
                    StatusMessage += " The file parsed but contained no clash results.";
                    return;
                }

                int withoutId = _items.Count(i => !i.ObjectA.HasElementId || !i.ObjectB.HasElementId);
                if (withoutId == _items.Count)
                {
                    StatusMessage += " No element ids in this report - the add-in will fall back to the " +
                                     "clash point. For exact results re-export with Element ID ticked in " +
                                     "Clash Detective > Report > Contents.";
                }

                // Look everything up straight away: the user asked for a clash list, and a list
                // full of Navisworks material names is not one. This also tells them immediately
                // whether the report even belongs to the model they have open.
                ResolveAll();
            }
            catch (Exception exception)
            {
                StatusMessage = "Could not read the report: " + exception.Message;
                _dialogs.ShowMessage("Clash Navigator", "Could not read the report.\n\n" + exception.Message);
            }
        }

        private void ShowSelectedClash()
        {
            RunOnSelected(false);
        }

        private void IsolateSelectedClash()
        {
            RunOnSelected(true);
        }

        private void RunOnSelected(bool forceIsolate)
        {
            ClashRowViewModel row = SelectedClash;
            if (row == null) return;

            NavigationSettings settings = BuildSettings();
            if (forceIsolate) settings.IsolateElements = true;

            StatusMessage = "Opening " + row.Name + " ...";

            RevitTask.Run(application =>
            {
                UIDocument uiDocument = application.ActiveUIDocument;
                if (uiDocument == null)
                {
                    StatusMessage = "No Revit model is open.";
                    return;
                }

                var service = new ClashNavigationService(uiDocument);
                NavigationResult result = service.ShowClash(row.Clash, settings);

                if (result.Resolution != null) row.ApplyResolution(result.Resolution);
                else row.State = "Not found";

                StatusMessage = result.Message;
            });
        }

        private void ResetView()
        {
            RevitTask.Run(application =>
            {
                UIDocument uiDocument = application.ActiveUIDocument;
                if (uiDocument == null) return;

                new ClashNavigationService(uiDocument).ResetView();
                StatusMessage = "Section box and temporary isolate cleared.";
            });
        }

        // Walks the whole list once and fills in the real Revit element for every clash. Runs
        // automatically after loading a report, and can be run again by hand after loading or
        // reloading a link.
        private void ResolveAll()
        {
            List<ClashRowViewModel> rows = Clashes.ToList();
            if (rows.Count == 0) return;

            NavigationSettings settings = BuildSettings();

            RevitTask.Run(application =>
            {
                UIDocument uiDocument = application.ActiveUIDocument;
                if (uiDocument == null)
                {
                    StatusMessage = "No Revit model is open - open the model this report was made from.";
                    return;
                }

                // One service for the whole run: it collects the links once instead of per row.
                var service = new ClashNavigationService(uiDocument);

                int both = 0;
                int partial = 0;
                int missing = 0;

                foreach (ClashRowViewModel row in rows)
                {
                    // allowGeometricSearch is off here - the slow fallback is fine for one clash
                    // the user clicked, but not for hundreds in a row.
                    ClashResolution resolution = service.Resolve(row.Clash, settings, false);
                    row.ApplyResolution(resolution);

                    if (resolution.FoundBoth) both++;
                    else if (resolution.FoundNone) missing++;
                    else partial++;
                }

                // The group headings are built from the Revit element names, which only exist
                // after this run, so the grouping has to be rebuilt now.
                ApplyGrouping();
                RefreshFilter();

                StatusMessage = rows.Count + " clashes checked against " + uiDocument.Document.Title
                              + " - " + both + " with both elements, " + partial + " with one side only, "
                              + missing + " no longer in the model.";
            });
        }

        // ---------- grouping and saving ----------

        private void ApplyGrouping()
        {
            if (ClashesView == null) return;

            ClashesView.GroupDescriptions.Clear();

            if (_groupDuplicates)
                ClashesView.GroupDescriptions.Add(new PropertyGroupDescription("DuplicateKey"));
        }

        // Every status change is written straight to the companion file. The file is tiny and
        // saving on each click means nothing is ever lost to a crash or a closed window.
        private void OnRowPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != "Status" && e.PropertyName != "AssignedTo") return;

            if (e.PropertyName == "AssignedTo")
            {
                var row = sender as ClashRowViewModel;
                if (row != null) RememberAssignee(row.AssignedTo);
            }

            int saved = ClashStatusStore.Save(ReportPath, _items);
            StatusMessage = saved == 0
                ? "All statuses are back to what the report said."
                : saved + " status change" + (saved == 1 ? "" : "s") + " saved next to the report.";
        }

        private void RememberAssignee(string name)
        {
            string trimmed = (name ?? string.Empty).Trim();
            if (trimmed.Length == 0) return;
            if (AssigneeValues.Any(v => string.Equals(v, trimmed, StringComparison.OrdinalIgnoreCase))) return;

            AssigneeValues.Add(trimmed);
        }

        // One decision, many rows. When the same pair of element types clashes thirty-eight
        // times it is one coordination issue with one owner, and nobody should type that owner
        // thirty-eight times.
        private void ApplyToDuplicates()
        {
            ClashRowViewModel source = SelectedClash;
            if (source == null) return;

            string key = source.DuplicateKey;
            int changed = 0;

            foreach (ClashRowViewModel row in Clashes)
            {
                if (row == source) continue;
                if (row.DuplicateKey != key) continue;

                row.Status = source.Status;
                row.AssignedTo = source.AssignedTo;
                changed++;
            }

            StatusMessage = changed == 0
                ? "No other clash has the same pair of element types."
                : "Applied '" + source.Status + "'"
                  + (string.IsNullOrEmpty(source.AssignedTo) ? "" : " / '" + source.AssignedTo + "'")
                  + " to " + changed + " duplicate clash" + (changed == 1 ? "" : "es") + ".";
        }

        // Copies the whole clash as a plain text block - the message a coordinator actually sends
        // when they hand a clash to whoever has to fix it.
        private void CopyDetails()
        {
            ClashRowViewModel row = SelectedClash;
            if (row == null) return;

            string reportName = string.IsNullOrEmpty(ReportPath)
                ? string.Empty
                : Path.GetFileName(ReportPath);

            StatusMessage = _dialogs.CopyToClipboard(row.BuildDetailBlock(reportName))
                ? "Clash details copied - paste them into an email, a chat or a cell."
                : "The clipboard is busy - try again.";
        }

        // Copies one side's element id on its own, so it can be pasted into
        // Manage > Inquiry > Select by ID or sent to whoever has to fix that element.
        private void CopyId(bool sideA)
        {
            ClashRowViewModel row = SelectedClash;
            if (row == null) return;

            string side = sideA ? "A" : "B";
            string id = sideA ? row.IdA : row.IdB;

            if (string.IsNullOrEmpty(id) || id == "-")
            {
                StatusMessage = "Element " + side + " of this clash has no id to copy.";
                return;
            }

            StatusMessage = _dialogs.CopyToClipboard(id)
                ? "Copied element " + side + " id: " + id
                : "The clipboard is busy - try again.";
        }

        private NavigationSettings BuildSettings()
        {
            return new NavigationSettings
            {
                PaddingMeters = PaddingMeters,

                // The Revit NWC exporter writes shared coordinates by default, which is also what
                // any coordinated project uses, so that is what the tool assumes.
                CoordinateBasis = ClashCoordinateBasis.Shared,

                SelectElements = true,
                IsolateElements = IsolateOnShow,
                ZoomToClash = ZoomOnShow
            };
        }

        // ---------- filtering ----------

        private bool PassesFilter(object item)
        {
            var row = item as ClashRowViewModel;
            if (row == null) return false;

            if (SelectedTest != AllFilter && row.TestName != SelectedTest) return false;
            if (SelectedStatus != AllFilter && row.Status != SelectedStatus) return false;

            return row.Matches(SearchText);
        }

        private void RefreshFilter()
        {
            if (ClashesView != null) ClashesView.Refresh();
            OnPropertyChanged("CountSummary");
        }

        private void RebuildFilterOptions()
        {
            TestOptions.Clear();
            TestOptions.Add(AllFilter);
            foreach (string test in Clashes.Select(c => c.TestName)
                                           .Where(v => !string.IsNullOrEmpty(v))
                                           .Distinct().OrderBy(v => v))
            {
                TestOptions.Add(test);
            }

            // The status filter always offers the full set, not only the statuses that happen to
            // be in the file - the user can change a status and still filter by it afterwards.
            StatusOptions.Clear();
            StatusOptions.Add(AllFilter);
            foreach (string status in CanonicalStatuses) StatusOptions.Add(status);

            foreach (string extra in Clashes.Select(c => c.Status)
                                            .Where(v => !string.IsNullOrEmpty(v))
                                            .Distinct()
                                            .Where(v => !CanonicalStatuses.Contains(v))
                                            .OrderBy(v => v))
            {
                StatusOptions.Add(extra);
            }

            foreach (string assignee in Clashes.Select(c => c.AssignedTo)
                                               .Where(v => !string.IsNullOrEmpty(v))
                                               .Distinct().OrderBy(v => v))
            {
                RememberAssignee(assignee);
            }

            _selectedTest = AllFilter;
            _selectedStatus = AllFilter;

            OnPropertyChanged("SelectedTest");
            OnPropertyChanged("SelectedStatus");
        }

        // ---------- report out ----------

        // Writes the clash list, with whatever statuses the coordinator set in this session,
        // as a CSV that opens straight in Excel.
        private void ExportReport()
        {
            string suggested = Path.GetFileNameWithoutExtension(ReportPath) + " - ClashNavigator.csv";
            string path = _dialogs.AskForSavePath("Export the clash report", "CSV file (*.csv)|*.csv", suggested);
            if (string.IsNullOrEmpty(path)) return;

            try
            {
                var text = new StringBuilder();
                text.AppendLine("Index,Test,Group,Clash,Status,Assigned to,Status edited,Type,Distance (m)," +
                                "Grid / Level,Element A Id,Element A,Model A," +
                                "Element B Id,Element B,Model B,State");

                var rows = ClashesView.Cast<ClashRowViewModel>().ToList();

                foreach (ClashRowViewModel row in rows)
                {
                    text.AppendLine(string.Join(",", new[]
                    {
                        Csv(row.Index.ToString()), Csv(row.TestName), Csv(row.GroupName), Csv(row.Name),
                        Csv(row.Status), Csv(row.AssignedTo),
                        Csv(row.IsStatusEdited || row.IsAssignmentEdited ? "yes" : ""), Csv(row.ClashType),
                        Csv(row.Distance), Csv(row.GridLocation),
                        Csv(row.IdA), Csv(row.ElementA), Csv(row.ModelA),
                        Csv(row.IdB), Csv(row.ElementB), Csv(row.ModelB),
                        Csv(row.State)
                    }));
                }

                // UTF-8 with a BOM so Excel opens it without mangling the text.
                File.WriteAllText(path, text.ToString(), new UTF8Encoding(true));

                int edited = rows.Count(r => r.IsStatusEdited || r.IsAssignmentEdited);
                StatusMessage = "Exported " + rows.Count + " clashes"
                              + (edited > 0 ? " (" + edited + " you reviewed)" : string.Empty)
                              + " to " + path;
            }
            catch (Exception exception)
            {
                StatusMessage = "Export failed: " + exception.Message;
            }
        }

        private static string Csv(string value)
        {
            if (string.IsNullOrEmpty(value)) return "\"\"";
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }
    }
}
