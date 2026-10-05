using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using DLSSVersionToolkit.Core.Models;

namespace DLSSVersionToolkit.ViewModels;

/// <summary>
/// Collects the outcome of one Update All run as discrete steps and persists each run to
/// %AppData%\DLSSVersionToolkit\runs. Collapsing 68 MessageBox call sites into one reporter is
/// the "detector and applier must share one function" lesson applied to reporting — success
/// evidence that vanishes on OK is the same defect class that hid a zero-file Streamline sync
/// for five releases (v0.0.42).
/// </summary>
public sealed class UpdateRunReportManager
{
    private readonly ObservableCollection<UpdateRunStep> _steps = new();
    private UpdateRunReport? _current;

    public ObservableCollection<UpdateRunStep> Steps => _steps;
    public bool HasSteps => _steps.Count > 0;
    public string LastReportPath { get; private set; } = "";
    public string RunsDirectory { get; set; } = DefaultRunsDir();

    private static string DefaultRunsDir() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "DLSSVersionToolkit", "runs");

    /// <summary>Begin a fresh run, replacing whatever was open before.</summary>
    public void Begin(string appVersion)
    {
        _current = new UpdateRunReport { StartedAt = DateTime.Now, AppVersion = appVersion };
        _steps.Clear();
    }

    /// <summary>Record one step result. Empty detail is dropped; detail is only for humans.</summary>
    public void Add(string name, string status, string detail = "")
    {
        _steps.Add(new UpdateRunStep { Name = name, Status = status, Detail = detail });
    }

    /// <summary>Finish and persist the run to disk. Non-fatal — a report write never breaks a run.</summary>
    public void Finish()
    {
        if (_current == null) return;
        _current.FinishedAt = DateTime.Now;

        // A run that never recorded a step (blocked at pre-flight: no network, no disk space)
        // is not a run — persisting an empty report would show an empty "LAST RUN" drawer.
        if (_steps.Count == 0)
        {
            _current = null;
            return;
        }

        try
        {
            var report = _current;
            LastReportPath = RunReportStore.Save(report, RunsDirectory) ?? LastReportPath;
        }
        catch { /* never let report persistence break the app */ }
        _current = null;
    }

}