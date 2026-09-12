using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using Clio.Common.McpWorker;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

/// <summary>
/// ENG-95262: coverage for the Windows worker's inherited-handle narrowing —
/// <c>STARTUPINFOEX</c> plus a <c>PROC_THREAD_ATTRIBUTE_HANDLE_LIST</c>, which stops a concurrently
/// launched worker from inheriting (and therefore keeping alive) a SIBLING's stdout/stderr pipe write
/// end. A retained write end means the sibling's reader never sees EOF, so the relay never fails its
/// pending calls and the parent waits on a worker that is already dead.
/// </summary>
/// <remarks>
/// <para>
/// <b>What these tests DO exercise on macOS and Linux:</b> the composition handed to
/// <c>CreateProcessW</c> — creation flags, the <c>cb</c> arithmetic that <c>EXTENDED_STARTUPINFO_PRESENT</c>
/// demands, and the exact handle set — plus the full unmanaged lifecycle of the attribute list against a
/// substituted native layer, including buffer sizing, teardown ordering and every failure path.
/// </para>
/// <para>
/// <b>What they CANNOT exercise off Windows:</b> that the kernel honours the list, i.e. that a concurrently
/// spawned worker really is denied a sibling's pipe handle. That is an observation about
/// <c>kernel32</c> and is only available on Windows.
/// <see cref="ProcThreadAttributeList_ShouldBuildAgainstRealKernel32_WhenRunningOnWindows"/> is the one
/// test that touches the real API; it reports an explicit, visible ignore on every other platform rather
/// than passing silently.
/// </para>
/// </remarks>
[TestFixture]
[Category("Unit")]
[Property("Module", "McpServer")]
public sealed class WindowsWorkerHandleInheritanceTests {

	private static readonly IntPtr StandardInputHandle = new(0x11);
	private static readonly IntPtr StandardOutputHandle = new(0x22);
	private static readonly IntPtr StandardErrorHandle = new(0x33);

	[Test]
	[Description("The child's inherited-handle list contains exactly the three standard-stream handles, in standard-stream order, and nothing else — that exhaustiveness is what keeps a sibling worker's pipe write end out of this child. Cross-platform: composition only.")]
	public void BuildInheritedHandleList_ShouldContainExactlyTheThreeStandardHandles_WhenAllThreeAreDistinct() {
		// Arrange
		IntPtr input = StandardInputHandle;
		IntPtr output = StandardOutputHandle;
		IntPtr error = StandardErrorHandle;

		// Act
		IntPtr[] handles = WindowsWorkerStartup.BuildInheritedHandleList(input, output, error);

		// Assert
		handles.Should().Equal([input, output, error],
			because: "the list is the exhaustive set the child may inherit: anything extra re-admits a sibling's pipe write end, and any omission of a handle named in STARTUPINFO fails the spawn with ERROR_INVALID_PARAMETER");
	}

	[Test]
	[Description("Two standard slots pointing at one handle produce one list entry, because a single entry already covers both slots while a duplicate entry is a malformed list. Cross-platform: composition only.")]
	public void BuildInheritedHandleList_ShouldCollapseTheDuplicate_WhenTwoStandardSlotsShareOneHandle() {
		// Arrange
		IntPtr shared = StandardOutputHandle;

		// Act
		IntPtr[] handles = WindowsWorkerStartup.BuildInheritedHandleList(StandardInputHandle, shared, shared);

		// Assert
		handles.Should().Equal([StandardInputHandle, shared],
			because: "merged output and error streams are one handle occupying two STARTUPINFO slots, and the list describes handles rather than slots");
		handles.Should().OnlyHaveUniqueItems(
			because: "a handle listed twice describes an inheritance set that does not exist");
	}

	[Test]
	[Description("A null or INVALID_HANDLE_VALUE standard handle is rejected while building the list, instead of being handed to CreateProcessW to fail there as ERROR_INVALID_PARAMETER with no indication of which stream was at fault. Cross-platform: composition only.")]
	public void BuildInheritedHandleList_ShouldThrow_WhenAStandardHandleIsInvalid() {
		// Arrange
		IntPtr invalidHandleValue = new(-1);

		// Act
		Action buildWithNullHandle = () => WindowsWorkerStartup.BuildInheritedHandleList(
			IntPtr.Zero, StandardOutputHandle, StandardErrorHandle);
		Action buildWithInvalidHandle = () => WindowsWorkerStartup.BuildInheritedHandleList(
			StandardInputHandle, StandardOutputHandle, invalidHandleValue);

		// Assert
		buildWithNullHandle.Should().Throw<ArgumentException>(
				because: "a null standard input handle is a defect in the pipe setup, and naming the stream in the failure is the difference between a one-line diagnosis and a native error code")
			.WithMessage("*standard input*");
		buildWithInvalidHandle.Should().Throw<ArgumentException>(
				because: "INVALID_HANDLE_VALUE in the list makes CreateProcessW fail for a reason the message would not otherwise reveal")
			.WithMessage("*standard error*");
	}

	[Test]
	[Description("The startup structure's cb describes STARTUPINFOEX rather than STARTUPINFO, which is what EXTENDED_STARTUPINFO_PRESENT requires; the wrong value fails every spawn with ERROR_INVALID_PARAMETER (87). Cross-platform: marshalling arithmetic only.")]
	public void BuildStartupInformation_ShouldSizeCbForTheExtendedStructure_WhenTheHandleListIsAttached() {
		// Arrange
		IntPtr attributeList = new(0x4242);

		// Act
		StartupInformationEx startupInformation = WindowsWorkerStartup.BuildStartupInformation(
			attributeList, StandardInputHandle, StandardOutputHandle, StandardErrorHandle);

		// Assert
		startupInformation.StartupInfo.cb.Should().Be(Marshal.SizeOf<StartupInformationEx>(),
			because: "with EXTENDED_STARTUPINFO_PRESENT the kernel reads cb as the size of the EXTENDED structure, and a short cb fails the spawn with ERROR_INVALID_PARAMETER");
		startupInformation.StartupInfo.cb.Should().BeGreaterThan(Marshal.SizeOf<StartupInformation>(),
			because: "the extended structure carries the attribute-list pointer on top of STARTUPINFO, so the two sizes must not be interchangeable by accident");
		startupInformation.lpAttributeList.Should().Be(attributeList,
			because: "the attribute list is the only thing that narrows inheritance; an unset pointer silently restores the inherit-everything behaviour");
		(startupInformation.StartupInfo.dwFlags & WindowsWorkerStartup.StartFlagUseStdHandles).Should()
			.Be(WindowsWorkerStartup.StartFlagUseStdHandles,
				because: "without STARTF_USESTDHANDLES the three hStd fields are ignored and the child gets no pipes at all");
		startupInformation.StartupInfo.hStdInput.Should().Be(StandardInputHandle,
			because: "the child reads its MCP requests from this handle");
		startupInformation.StartupInfo.hStdOutput.Should().Be(StandardOutputHandle,
			because: "the relay's EOF completion signal is the closing of this handle's write end");
		startupInformation.StartupInfo.hStdError.Should().Be(StandardErrorHandle,
			because: "the worker's diagnostics must reach the parent rather than the parent's own console");
	}

	[Test]
	[Description("The creation flags keep CREATE_SUSPENDED — the containment property ADR section 2.4 measured — while adding EXTENDED_STARTUPINFO_PRESENT for the handle list. Cross-platform: flag composition only.")]
	public void CreationFlags_ShouldKeepCreateSuspendedAndAddExtendedStartupInfo_WhenTheHandleListIsInUse() {
		// Arrange
		uint flags = WindowsWorkerStartup.CreationFlags;

		// Act
		bool createsSuspended = (flags & WindowsWorkerStartup.CreateSuspended) != 0;
		bool carriesExtendedStartupInfo = (flags & WindowsWorkerStartup.ExtendedStartupInfoPresent) != 0;

		// Assert
		createsSuspended.Should().BeTrue(
			because: "ADR section 2.4 measured that a child assigned to the job AFTER it started leaks a grandchild past the parent's force-kill; only CREATE_SUSPENDED puts it in the job before its first instruction");
		carriesExtendedStartupInfo.Should().BeTrue(
			because: "without EXTENDED_STARTUPINFO_PRESENT the kernel reads only the plain STARTUPINFO and ignores the attribute list, so the child would inherit every inheritable handle again");
		(flags & WindowsWorkerStartup.CreateUnicodeEnvironment).Should().NotBe(0,
			because: "the environment block is built as UTF-16 and would be read as ANSI without this flag");
		(flags & WindowsWorkerStartup.CreateNoWindow).Should().NotBe(0,
			because: "a worker must not flash a console window on the user's desktop");
	}

	[Test]
	[Description("The attribute list buffer is allocated with exactly the size the deliberately failing sizing call reported, and the handle list attribute is written into it with a byte count rather than a handle count. Cross-platform: the native layer is substituted.")]
	public void CreateForInheritedHandles_ShouldAllocateTheSizeTheSizingCallReported_WhenTheListIsBuilt() {
		// Arrange
		RecordingProcThreadAttributeListNative native = new() { ReportedSize = 96 };
		IntPtr[] handles = [StandardInputHandle, StandardOutputHandle, StandardErrorHandle];

		// Act
		using ProcThreadAttributeList attributeList =
			ProcThreadAttributeList.CreateForInheritedHandles(handles, native);

		// Assert
		native.Operations.Should().Equal(["size", "allocate", "initialize", "update"],
			because: "the size is only knowable from the first call, so allocating before asking it — or asking it twice — means the buffer size was guessed");
		native.AllocatedByteCount.Should().Be(96,
			because: "a buffer smaller than the reported size is a heap overflow the moment the kernel writes the list into it");
		native.SizingCallAttributeList.Should().Be(IntPtr.Zero,
			because: "the sizing call is made with a null list precisely so that it fails and reports the size");
		native.AttributeCounts.Should().AllBeEquivalentTo(1,
			because: "the list carries exactly one attribute, PROC_THREAD_ATTRIBUTE_HANDLE_LIST, and a smaller count would leave no room for it");
		native.UpdateAttribute.Should().Be(new IntPtr(0x00020002),
			because: "PROC_THREAD_ATTRIBUTE_HANDLE_LIST is ProcThreadAttributeValue(2, input) = 2 | 0x00020000; any other value configures a different attribute and leaves inheritance unrestricted");
		native.UpdateValueSize.Should().Be(new IntPtr(3 * IntPtr.Size),
			because: "UpdateProcThreadAttribute takes the value size in BYTES, and passing the handle count instead would describe a truncated list");
		attributeList.Handle.Should().Be(native.AllocatedBuffer,
			because: "STARTUPINFOEX must point at the buffer that was actually initialized");
	}

	[Test]
	[Description("The attribute stores a pointer to the pinned handle array rather than a copy, so the array's contents must be readable through that pointer and the pin must still be held while the list is alive. Cross-platform: the native layer is substituted.")]
	public void CreateForInheritedHandles_ShouldPointTheAttributeAtThePinnedHandles_WhileTheListIsAlive() {
		// Arrange
		RecordingProcThreadAttributeListNative native = new();
		IntPtr[] handles = [StandardInputHandle, StandardOutputHandle, StandardErrorHandle];

		// Act
		using ProcThreadAttributeList attributeList =
			ProcThreadAttributeList.CreateForInheritedHandles(handles, native);
		IntPtr[] handlesSeenByTheKernel = new IntPtr[handles.Length];
		Marshal.Copy(native.UpdateValue, handlesSeenByTheKernel, 0, handles.Length);

		// Assert
		handlesSeenByTheKernel.Should().Equal(handles,
			because: "UpdateProcThreadAttribute stores the POINTER, so what the kernel later reads is whatever is at that address when CreateProcess runs — not a snapshot taken now");
		attributeList.HandlesPinned.Should().BeTrue(
			because: "an unpinned array can be moved by a collection between the update and the spawn, leaving the kernel reading an address that no longer holds handles");
	}

	[Test]
	[Description("Disposing destroys the attribute list before freeing the memory that holds it, and only then releases the pin — freeing first, or unpinning first, is undefined behaviour. Cross-platform: the native layer is substituted.")]
	public void Dispose_ShouldDeleteTheListBeforeFreeingItsBuffer_WhenTheListIsDisposed() {
		// Arrange
		RecordingProcThreadAttributeListNative native = new();
		IntPtr[] handles = [StandardInputHandle, StandardOutputHandle, StandardErrorHandle];
		ProcThreadAttributeList attributeList = ProcThreadAttributeList.CreateForInheritedHandles(handles, native);

		// Act
		attributeList.Dispose();

		// Assert
		native.Operations.Should().Equal(["size", "allocate", "initialize", "update", "delete", "free"],
			because: "DeleteProcThreadAttributeList reads the buffer it is destroying, so freeing that buffer first is a use-after-free");
		attributeList.HandlesPinned.Should().BeFalse(
			because: "the pin outlives the list on purpose and must be released once nothing can dereference the array, or the process leaks pinned memory per worker launch");
		attributeList.Handle.Should().Be(IntPtr.Zero,
			because: "a disposed list must not keep handing out a pointer into freed memory");
	}

	[Test]
	[Description("Disposing twice destroys and frees once, because a second DeleteProcThreadAttributeList or FreeHGlobal on the same pointer corrupts the heap. Cross-platform: the native layer is substituted.")]
	public void Dispose_ShouldReleaseEverythingOnce_WhenCalledTwice() {
		// Arrange
		RecordingProcThreadAttributeListNative native = new();
		IntPtr[] handles = [StandardInputHandle, StandardOutputHandle, StandardErrorHandle];
		ProcThreadAttributeList attributeList = ProcThreadAttributeList.CreateForInheritedHandles(handles, native);

		// Act
		attributeList.Dispose();
		attributeList.Dispose();

		// Assert
		native.Operations.Should().ContainSingle(operation => operation == "delete",
			because: "destroying an already destroyed attribute list is undefined, and a using block around a list disposed elsewhere is an ordinary way to reach that");
		native.Operations.Should().ContainSingle(operation => operation == "free",
			because: "a double free corrupts the heap of the long-lived parent process, which is a far worse outcome than the handle leak this class exists to prevent");
	}

	[Test]
	[Description("A failing UpdateProcThreadAttribute frees the buffer it had already allocated, and fails the spawn rather than falling back to unrestricted inheritance. Cross-platform: the native layer is substituted.")]
	public void CreateForInheritedHandles_ShouldDestroyAndFreeTheBuffer_WhenTheAttributeUpdateFails() {
		// Arrange
		RecordingProcThreadAttributeListNative native = new() { FailUpdate = true };
		IntPtr[] handles = [StandardInputHandle, StandardOutputHandle, StandardErrorHandle];

		// Act
		Action build = () => ProcThreadAttributeList.CreateForInheritedHandles(handles, native);

		// Assert
		build.Should().Throw<System.ComponentModel.Win32Exception>(
			because: "falling back to an unrestricted spawn would silently reintroduce the sibling-pipe retention this class removes, and the resulting hang would point at an unrelated process");
		native.Operations.Should().Equal(["size", "allocate", "initialize", "update", "delete", "free"],
			because: "the buffer was already allocated and initialized when the update failed, so the failure path owes both a destroy and a free");
	}

	[Test]
	[Description("A failure between allocating the buffer and initializing the list frees the buffer but does NOT destroy a list that was never initialized. Cross-platform: the native layer is substituted.")]
	public void CreateForInheritedHandles_ShouldFreeWithoutDestroying_WhenTheListWasNeverInitialized() {
		// Arrange
		RecordingProcThreadAttributeListNative native = new() { FailInitialize = true };
		IntPtr[] handles = [StandardInputHandle, StandardOutputHandle, StandardErrorHandle];

		// Act
		Action build = () => ProcThreadAttributeList.CreateForInheritedHandles(handles, native);

		// Assert
		build.Should().Throw<System.ComponentModel.Win32Exception>(
			because: "an uninitialized attribute list cannot restrict anything, so the spawn must fail loudly instead of proceeding without one");
		native.Operations.Should().Equal(["size", "allocate", "initialize", "free"],
			because: "DeleteProcThreadAttributeList on memory that was never initialized reads uninitialized bytes as a list structure; the buffer must still be freed");
	}

	[Test]
	[Description("A sizing call that reports nothing fails the spawn without allocating, so there is no buffer to leak. Cross-platform: the native layer is substituted.")]
	public void CreateForInheritedHandles_ShouldNotAllocate_WhenTheSizingCallReportsNoSize() {
		// Arrange
		RecordingProcThreadAttributeListNative native = new() { ReportedSize = 0 };
		IntPtr[] handles = [StandardInputHandle, StandardOutputHandle, StandardErrorHandle];

		// Act
		Action build = () => ProcThreadAttributeList.CreateForInheritedHandles(handles, native);

		// Assert
		build.Should().Throw<System.ComponentModel.Win32Exception>(
			because: "the reported size — not the boolean, which fails by design — is the sizing call's success signal, and a zero size means it did not succeed");
		native.Operations.Should().Equal(["size"],
			because: "allocating a zero or negative size would either throw from the allocator or hand the kernel a buffer that cannot hold the list");
	}

	[Test]
	[Description("An empty handle list is refused: a child created with inheritance enabled and an empty list is denied even its own standard streams. Cross-platform: composition only.")]
	public void CreateForInheritedHandles_ShouldThrow_WhenNoHandlesAreGiven() {
		// Arrange
		RecordingProcThreadAttributeListNative native = new();
		IntPtr[] handles = [];

		// Act
		Action build = () => ProcThreadAttributeList.CreateForInheritedHandles(handles, native);

		// Assert
		build.Should().Throw<ArgumentException>(
			because: "an empty list is not 'no restriction', it is 'inherit nothing', which leaves the worker unable to speak MCP at all");
		native.Operations.Should().BeEmpty(
			because: "the argument is rejected before any unmanaged memory is touched, so there is nothing to unwind");
	}

	[Test]
	[Description("WINDOWS-ONLY, explicitly ignored elsewhere: the attribute list is built, populated and destroyed against the real kernel32 with three real inheritable pipe handles. Off Windows this reports an ignore rather than passing silently, because no substitute can prove the kernel accepts the list.")]
	public void ProcThreadAttributeList_ShouldBuildAgainstRealKernel32_WhenRunningOnWindows() {
		// Arrange
		if (!OperatingSystem.IsWindows()) {
			Assert.Ignore(
				"PROC_THREAD_ATTRIBUTE_HANDLE_LIST is a kernel32 mechanism: whether the kernel accepts the list is only observable on Windows. Everything about its composition and lifecycle is asserted by the other tests in this fixture.");
		}
		using AnonymousPipeServerStream input = new(PipeDirection.Out, HandleInheritability.Inheritable);
		using AnonymousPipeServerStream output = new(PipeDirection.In, HandleInheritability.Inheritable);
		using AnonymousPipeServerStream error = new(PipeDirection.In, HandleInheritability.Inheritable);
		IntPtr[] handles = WindowsWorkerStartup.BuildInheritedHandleList(
			input.ClientSafePipeHandle.DangerousGetHandle(),
			output.ClientSafePipeHandle.DangerousGetHandle(),
			error.ClientSafePipeHandle.DangerousGetHandle());

		// Act
		ProcThreadAttributeList attributeList = ProcThreadAttributeList.CreateForInheritedHandles(handles);
		IntPtr listHandle = attributeList.Handle;
		bool pinnedWhileAlive = attributeList.HandlesPinned;
		attributeList.Dispose();

		// Assert
		handles.Should().HaveCount(3,
			because: "three distinct inheritable pipe client handles are exactly what a worker needs and all it may have");
		listHandle.Should().NotBe(IntPtr.Zero,
			because: "kernel32 accepted the size query and the initialization, which is the part no substitute can stand in for");
		pinnedWhileAlive.Should().BeTrue(
			because: "the real attribute now points at this array and must keep pointing at it until the list is destroyed");
		attributeList.HandlesPinned.Should().BeFalse(
			because: "a real DeleteProcThreadAttributeList has run, so nothing can dereference the array any more");
	}

	/// <summary>
	/// An <see cref="IProcThreadAttributeListNative"/> that records the ordered call sequence and its
	/// arguments, and can be made to fail at each step. Memory is really allocated and really freed, so a
	/// missing free shows up as an ordering assertion rather than as a silent leak in the test host.
	/// </summary>
	private sealed class RecordingProcThreadAttributeListNative : IProcThreadAttributeListNative {

		private const int ArbitraryNativeErrorCode = 8;

		internal List<string> Operations { get; } = [];

		internal List<int> AttributeCounts { get; } = [];

		internal int ReportedSize { get; init; } = 64;

		internal bool FailInitialize { get; init; }

		internal bool FailUpdate { get; init; }

		internal int AllocatedByteCount { get; private set; }

		internal IntPtr AllocatedBuffer { get; private set; }

		internal IntPtr SizingCallAttributeList { get; private set; }

		internal IntPtr UpdateAttribute { get; private set; }

		internal IntPtr UpdateValue { get; private set; }

		internal IntPtr UpdateValueSize { get; private set; }

		public int LastError => ArbitraryNativeErrorCode;

		public bool Initialize(IntPtr attributeList, int attributeCount, ref IntPtr size) {
			AttributeCounts.Add(attributeCount);
			if (attributeList == IntPtr.Zero) {
				// The real sizing call reports the required size and returns FALSE with
				// ERROR_INSUFFICIENT_BUFFER; the substitute must reproduce that, or the code under test
				// would be validated against a contract kernel32 does not honour.
				Operations.Add("size");
				SizingCallAttributeList = attributeList;
				size = new IntPtr(ReportedSize);
				return false;
			}
			Operations.Add("initialize");
			return !FailInitialize;
		}

		public bool Update(IntPtr attributeList, IntPtr attribute, IntPtr value, IntPtr valueSize) {
			Operations.Add("update");
			UpdateAttribute = attribute;
			UpdateValue = value;
			UpdateValueSize = valueSize;
			return !FailUpdate;
		}

		public void Delete(IntPtr attributeList) => Operations.Add("delete");

		public IntPtr Allocate(int byteCount) {
			Operations.Add("allocate");
			AllocatedByteCount = byteCount;
			AllocatedBuffer = Marshal.AllocHGlobal(byteCount);
			return AllocatedBuffer;
		}

		public void Free(IntPtr buffer) {
			Operations.Add("free");
			Marshal.FreeHGlobal(buffer);
		}
	}
}
