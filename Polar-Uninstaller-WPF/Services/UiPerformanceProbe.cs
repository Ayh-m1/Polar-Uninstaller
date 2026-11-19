using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
namespace PolarUninstallerWpf.Services;
/// <summary>Throttled scroll/load-to-render-dispatch latency, not physical GPU frame timing. No idle timer.</summary>
public static class UiPerformanceProbe
{
    private static readonly ConditionalWeakTable<FrameworkElement,Probe> Attached=new();
    public static void Attach(FrameworkElement view)=>Attached.GetValue(view,v=>new Probe(v));
    private sealed class Probe
    {
        private readonly FrameworkElement view;private long last;private int generation;private bool live,pending;
        public Probe(FrameworkElement view){this.view=view;view.Loaded+=Loaded;view.Unloaded+=Unloaded;}
        private void Loaded(object sender,RoutedEventArgs args){if(live)return;live=true;generation++;view.AddHandler(ScrollViewer.ScrollChangedEvent,new ScrollChangedEventHandler(Scrolled));Sample("list.render-dispatch");}
        private void Unloaded(object sender,RoutedEventArgs args){live=false;generation++;pending=false;view.RemoveHandler(ScrollViewer.ScrollChangedEvent,new ScrollChangedEventHandler(Scrolled));}
        private void Scrolled(object sender,ScrollChangedEventArgs args){long now=Environment.TickCount64;if(now-last<150)return;last=now;Sample("scroll.render-dispatch");}
        private void Sample(string name)
        {
            if(pending||view.Dispatcher.HasShutdownStarted)return;pending=true;int request=generation;long start=Stopwatch.GetTimestamp();
            view.Dispatcher.InvokeAsync(()=>{if(request!=generation)return;pending=false;if(live)PerformanceTrace.Record(view.GetType().Name+"."+name,Stopwatch.GetElapsedTime(start).TotalMilliseconds);},DispatcherPriority.Render);
        }
    }
}
