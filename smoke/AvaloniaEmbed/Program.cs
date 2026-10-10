// SPDX-FileCopyrightText: 2026 belshftl
// SPDX-License-Identifier: MIT

using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Themes.Fluent;

namespace AvaloniaEmbed;

public sealed class App : Application {
	public override void Initialize() => Styles.Add(new FluentTheme());

	public override void OnFrameworkInitializationCompleted() {
		if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
			desktop.MainWindow = new MainWindow(Program.Title);
		base.OnFrameworkInitializationCompleted();
	}
}

public static class Program {
	public static string Title { get; private set; } = "Injure in Avalonia";

	[STAThread]
	public static int Main(string[] args) {
		for (int i = 0; i < args.Length; i++) {
			if (args[i] == "--title" && i + 1 < args.Length) {
				Title = args[++i];
			} else {
				Console.Error.WriteLine("usage: AvaloniaEmbed [--title <window title>]");
				return 2;
			}
		}
		return AppBuilder.Configure<App>().UsePlatformDetect().StartWithClassicDesktopLifetime([]);
	}
}
