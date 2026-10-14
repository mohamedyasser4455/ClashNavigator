using System;
using System.Collections.Generic;
using Autodesk.Revit.UI;

namespace ClashNavigator.Core
{
    // The window is modeless, so the user can keep working in Revit while the clash list is open.
    // The price of that is this class: code running in a modeless window is NOT inside a Revit API
    // context, and touching the model from there throws. An ExternalEvent is the official way back
    // in - we hand Revit a job, Revit calls us back on its own thread when it is safe.
    public static class RevitTask
    {
        private class JobHandler : IExternalEventHandler
        {
            private readonly Queue<Action<UIApplication>> _jobs = new Queue<Action<UIApplication>>();

            public Action<string> ErrorReporter;

            public void Enqueue(Action<UIApplication> job)
            {
                lock (_jobs) _jobs.Enqueue(job);
            }

            public void Execute(UIApplication application)
            {
                while (true)
                {
                    Action<UIApplication> job;

                    lock (_jobs)
                    {
                        if (_jobs.Count == 0) return;
                        job = _jobs.Dequeue();
                    }

                    try
                    {
                        job(application);
                    }
                    catch (Exception exception)
                    {
                        // One bad clash must not kill the session - report it and keep going.
                        Action<string> reporter = ErrorReporter;
                        if (reporter != null) reporter(exception.Message);
                        else TaskDialog.Show("Clash Navigator", exception.Message);
                    }
                }
            }

            public string GetName()
            {
                return "Clash Navigator external event";
            }
        }

        private static JobHandler _handler;
        private static ExternalEvent _event;

        // Must be called from a real API context - the external command is the right place.
        public static void Initialize()
        {
            if (_event != null) return;

            _handler = new JobHandler();
            _event = ExternalEvent.Create(_handler);
        }

        public static void SetErrorReporter(Action<string> reporter)
        {
            if (_handler != null) _handler.ErrorReporter = reporter;
        }

        public static void Run(Action<UIApplication> job)
        {
            if (job == null) return;
            if (_event == null) throw new InvalidOperationException("RevitTask.Initialize() was never called.");

            _handler.Enqueue(job);
            _event.Raise();
        }
    }
}
