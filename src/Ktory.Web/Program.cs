using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;

namespace Ktory.Web;

public class Program
{
    public static async Task Main(string[] args)
    {
        var builder = WebAssemblyHostBuilder.CreateDefault(args);
        var host = builder.Build();

        var jsRuntime = host.Services.GetRequiredService<IJSRuntime>();
        await jsRuntime.InvokeVoidAsync("window.registerKtoryWasmBridge", DotNetObjectReference.Create(new KtoryWasmBridge()));

        await host.RunAsync();
    }
}
