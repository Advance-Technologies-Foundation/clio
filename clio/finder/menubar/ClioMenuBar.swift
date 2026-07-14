import AppKit
import Foundation
import UniformTypeIdentifiers

// ClioMenuBar - a lightweight macOS menu bar (status bar) agent for clio.
//
// It shows a status item in the top menu bar with:
//   * "Deploy Creatio..."  -> pick a folder or .zip, then run `clio deploy-creatio`
//   * one submenu per registered Creatio host (from `clio hosts --json`) with
//     Start / Stop / Open folder actions
//   * Refresh and Quit
//
// The clio executable path is passed as the first launch argument by the
// installed LaunchAgent; if absent, common locations and PATH are searched.

final class ClioController: NSObject, NSMenuDelegate {

	private let statusItem = NSStatusBar.system.statusItem(withLength: NSStatusItem.variableLength)
	private let clioPath: String

	private var cachedHosts: [HostInfo] = []
	private var isRefreshing = false
	private var hasLoadedOnce = false

	struct HostInfo: Decodable {
		let Environment: String?
		let ServiceName: String?
		let Status: String?
		let PID: Int?
		let EnvironmentPath: String?
		let Url: String?
	}

	init(clioPath: String) {
		self.clioPath = clioPath
		super.init()
		configureButton()
		let menu = NSMenu()
		menu.delegate = self
		statusItem.menu = menu
		rebuildMenu(menu)
		refreshHostsAsync()
	}

	private func configureButton() {
		guard let button = statusItem.button else { return }
		if let image = NSImage(systemSymbolName: "shippingbox", accessibilityDescription: "Clio") {
			image.isTemplate = true
			button.image = image
		} else {
			button.title = "Clio"
		}
	}

	// MARK: - Menu building

	func menuNeedsUpdate(_ menu: NSMenu) {
		rebuildMenu(menu)
		refreshHostsAsync()
	}

	private func rebuildMenu(_ menu: NSMenu) {
		menu.removeAllItems()

		let deploy = NSMenuItem(title: "Deploy Creatio...",
			action: #selector(deployAction), keyEquivalent: "")
		deploy.target = self
		menu.addItem(deploy)

		menu.addItem(.separator())

		let header = NSMenuItem(title: "Creatio Hosts", action: nil, keyEquivalent: "")
		header.isEnabled = false
		menu.addItem(header)

		if cachedHosts.isEmpty {
			let title = hasLoadedOnce ? "  No registered hosts" : "  Loading hosts…"
			let placeholder = NSMenuItem(title: title, action: nil, keyEquivalent: "")
			placeholder.isEnabled = false
			menu.addItem(placeholder)
		} else {
			for host in cachedHosts {
				menu.addItem(makeHostItem(host))
			}
		}

		menu.addItem(.separator())

		let refresh = NSMenuItem(title: "Refresh", action: #selector(refreshAction), keyEquivalent: "r")
		refresh.target = self
		menu.addItem(refresh)

		let quit = NSMenuItem(title: "Quit", action: #selector(quitAction), keyEquivalent: "q")
		quit.target = self
		menu.addItem(quit)
	}

	private func makeHostItem(_ host: HostInfo) -> NSMenuItem {
		let name = host.Environment ?? "unknown"
		let status = host.Status ?? "Unknown"
		let running = status.lowercased().contains("running")
		let item = NSMenuItem(title: "\(running ? "\u{25CF}" : "\u{25CB}") \(name) — \(status)",
			action: nil, keyEquivalent: "")

		let submenu = NSMenu()

		let start = NSMenuItem(title: "Start", action: #selector(startAction(_:)), keyEquivalent: "")
		start.target = self
		start.representedObject = name
		start.isEnabled = !running
		submenu.addItem(start)

		let stop = NSMenuItem(title: "Stop", action: #selector(stopAction(_:)), keyEquivalent: "")
		stop.target = self
		stop.representedObject = name
		stop.isEnabled = running
		submenu.addItem(stop)

		if let url = host.Url, !url.isEmpty {
			submenu.addItem(.separator())
			let openSite = NSMenuItem(title: "Open in browser",
				action: #selector(openSiteAction(_:)), keyEquivalent: "")
			openSite.target = self
			openSite.representedObject = url
			submenu.addItem(openSite)
		}

		if let path = host.EnvironmentPath, !path.isEmpty {
			if host.Url == nil || host.Url?.isEmpty == true {
				submenu.addItem(.separator())
			}
			let open = NSMenuItem(title: "Open folder", action: #selector(openFolderAction(_:)),
				keyEquivalent: "")
			open.target = self
			open.representedObject = path
			submenu.addItem(open)
		}

		submenu.addItem(.separator())
		let remove = NSMenuItem(title: "Remove host…", action: #selector(removeHostAction(_:)),
			keyEquivalent: "")
		remove.target = self
		remove.representedObject = name
		submenu.addItem(remove)

		item.submenu = submenu
		return item
	}

	// MARK: - Actions

	@objc private func deployAction() {
		let panel = NSOpenPanel()
		panel.title = "Select a Creatio folder or .zip to deploy"
		panel.canChooseDirectories = true
		panel.canChooseFiles = true
		panel.allowsMultipleSelection = false
		if #available(macOS 11.0, *) {
			panel.allowedContentTypes = [UTType.zip, UTType.folder]
		}
		NSApp.activate(ignoringOtherApps: true)
		guard panel.runModal() == .OK, let url = panel.url else { return }
		let path = url.path
		let quoted = shellQuote(path)
		runInTerminal("\(shellQuote(clioPath)) deploy-creatio --zip-file \(quoted) --explorer-launch")
	}

	@objc private func startAction(_ sender: NSMenuItem) {
		guard let env = sender.representedObject as? String else { return }
		runInTerminal("\(shellQuote(clioPath)) start -e \(shellQuote(env))")
	}

	@objc private func stopAction(_ sender: NSMenuItem) {
		guard let env = sender.representedObject as? String else { return }
		runInTerminal("\(shellQuote(clioPath)) stop -e \(shellQuote(env))")
	}

	@objc private func openSiteAction(_ sender: NSMenuItem) {
		guard let raw = sender.representedObject as? String,
			  let url = URL(string: raw) else { return }
		NSWorkspace.shared.open(url)
	}

	@objc private func openFolderAction(_ sender: NSMenuItem) {
		guard let path = sender.representedObject as? String else { return }
		NSWorkspace.shared.selectFile(nil, inFileViewerRootedAtPath: path)
	}

	@objc private func removeHostAction(_ sender: NSMenuItem) {
		guard let env = sender.representedObject as? String else { return }
		let alert = NSAlert()
		alert.alertStyle = .critical
		alert.messageText = "Remove host \"\(env)\"?"
		alert.informativeText = "This runs 'clio uninstall-creatio' which permanently drops the "
			+ "database and deletes the installation files for this environment. This cannot be undone."
		alert.addButton(withTitle: "Remove")
		alert.addButton(withTitle: "Cancel")
		NSApp.activate(ignoringOtherApps: true)
		guard alert.runModal() == .alertFirstButtonReturn else { return }
		runInTerminal("\(shellQuote(clioPath)) uninstall-creatio -e \(shellQuote(env))")
	}

	@objc private func refreshAction() {
		refreshHostsAsync()
	}

	private func refreshHostsAsync() {
		if isRefreshing { return }
		isRefreshing = true
		DispatchQueue.global(qos: .userInitiated).async { [weak self] in
			guard let self = self else { return }
			let hosts = self.loadHosts()
			DispatchQueue.main.async {
				self.cachedHosts = hosts
				self.hasLoadedOnce = true
				self.isRefreshing = false
				if let menu = self.statusItem.menu { self.rebuildMenu(menu) }
			}
		}
	}

	@objc private func quitAction() {
		NSApp.terminate(nil)
	}

	// MARK: - clio invocation

	private func loadHosts() -> [HostInfo] {
		guard let output = runClioCapture(["hosts", "--json"]),
			  let start = output.firstIndex(of: "["),
			  let end = output.lastIndex(of: "]"),
			  let data = String(output[start...end]).data(using: .utf8),
			  let hosts = try? JSONDecoder().decode([HostInfo].self, from: data) else {
			return []
		}
		return hosts
	}

	private func runClioCapture(_ arguments: [String]) -> String? {
		let command = ([clioPath] + arguments).map { shellQuote($0) }.joined(separator: " ")
		let process = Process()
		process.executableURL = URL(fileURLWithPath: "/bin/zsh")
		process.arguments = ["-lc", command]
		let pipe = Pipe()
		process.standardOutput = pipe
		process.standardError = Pipe()
		do {
			try process.run()
		} catch {
			return nil
		}
		let data = pipe.fileHandleForReading.readDataToEndOfFile()
		process.waitUntilExit()
		return String(data: data, encoding: .utf8)
	}

	private func runInTerminal(_ command: String) {
		let escaped = command.replacingOccurrences(of: "\\", with: "\\\\")
			.replacingOccurrences(of: "\"", with: "\\\"")
		let script = "tell application \"Terminal\"\nactivate\ndo script \"\(escaped)\"\nend tell"
		let process = Process()
		process.executableURL = URL(fileURLWithPath: "/usr/bin/osascript")
		process.arguments = ["-e", script]
		try? process.run()
	}

	private func shellQuote(_ value: String) -> String {
		"'" + value.replacingOccurrences(of: "'", with: "'\\''") + "'"
	}
}

// MARK: - Entry point

func resolveClioPath() -> String {
	let args = CommandLine.arguments
	if args.count > 1, FileManager.default.isExecutableFile(atPath: args[1]) {
		return args[1]
	}
	let home = FileManager.default.homeDirectoryForCurrentUser.path
	let candidates = [
		"\(home)/.dotnet/tools/clio",
		"/usr/local/bin/clio",
		"/opt/homebrew/bin/clio"
	]
	for candidate in candidates where FileManager.default.isExecutableFile(atPath: candidate) {
		return candidate
	}
	return "clio"
}

let app = NSApplication.shared
app.setActivationPolicy(.accessory)
let controller = ClioController(clioPath: resolveClioPath())
app.run()
