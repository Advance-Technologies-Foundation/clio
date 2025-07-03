using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions.TestingHelpers;
using System.IO.Compression;
using System.Linq;
using System.Reflection.Emit;
using Autofac;
using Clio.Common;
using Clio.ComposableApplication;
using Clio.Package;
using Clio.Tests.Command;
using Clio.Tests.Extensions;
using Common.Logging;
using FluentAssertions;
using FluentValidation;
using Newtonsoft.Json;
using NSubstitute;
using NUnit.Framework;
using FileSystem = System.IO.Abstractions.FileSystem;
using IZipFile = Clio.Common.IZipFile;

namespace Clio.Tests.ComposableApplication;

public class ComposableApplicationManagerTestCase : BaseClioModuleTests
{

	#region Constants: Private

        private static readonly string IconPath = Path.Combine(Path.GetTempPath(), "Partner.svg");

	private const string PartnerSvgBase64
		= "PHN2ZyB3aWR0aD0iMjI5IiBoZWlnaHQ9IjIxOSIgdmlld0JveD0iMCAwIDIyOSAyMTkiIGZpbGw9Im5vbmUiIHhtbG5zPSJodHRwOi8vd3d3LnczLm9yZy8yMDAwL3N2ZyI+DQo8ZyBjbGlwLXBhdGg9InVybCgjY2xpcDBfMTczXzIpIj4NCjxwYXRoIGZpbGwtcnVsZT0iZXZlbm9kZCIgY2xpcC1ydWxlPSJldmVub2RkIiBkPSJNMTAyLjAzIDBDODcuMzYgNC42MiA3Mi42OSA5LjIzIDU4LjAzIDEzLjgzQzU5LjY2IDMxLjc0IDYxLjI5IDQ5LjY2IDYyLjkxIDY3LjU3Qzc0LjQxIDc1LjE5IDg1Ljg3IDgyLjg3IDk3LjQ0IDkwLjM4QzEwMS43NiA4NS40MyAxMDMuOTEgODQuNSAxMTAuNDggODMuNTRDMTA5LjY4IDc4LjM3IDEwOC45IDczLjE3IDEwOC4xNCA2OEwxMDYuOTkgNjcuNTRDOTcuNDIgNjMuNjEgODguNDMgNTQuMDIgODcuMTEgNDMuNTRDODUuNjYgMzIuMDQgOTQuMzYgMjYuMjYgMTA1LjEzIDI4LjQxQzExNi40IDMwLjY1IDEyNy42IDQxLjE3IDEyOS44MyA1Mi41NUMxMzEuNDYgNjAuOTUgMTI3LjY0IDY4LjIyIDExOC44MiA2OS40MUwxMTYuNTggNjkuNzFDMTE3LjQ3IDc0Ljg3IDExOC4zNCA4MCAxMTkuMTkgODUuMTZDMTI1LjM2IDg3LjY0IDEyOC44MSA5MC4zIDEzMy4zNiA5NS4xMkMxNDEuOTggOTAuNTIgMTUwLjU5IDg1Ljg5IDE1OS4xOSA4MS4yNUMxNTUuNSA2NC42MiAxNTEuODEgNDggMTQ4LjEgMzEuMzdDMTMyLjc1IDIwLjk1IDExNy4zOSAxMC41MSAxMDIuMDYgMC4wM0wxMDIuMDQgMC4wMUgxMDIuMDNWMFoiIGZpbGw9IndoaXRlIi8+DQo8cGF0aCBmaWxsLXJ1bGU9ImV2ZW5vZGQiIGNsaXAtcnVsZT0iZXZlbm9kZCIgZD0iTTE2My4zOSA4Ni45NDk5QzE1NC44NCA5MS43ODk5IDE0Ni4yNyA5Ni42Mjk5IDEzNy43NCAxMDEuNTNDMTQwLjYgMTA3LjU4IDE0MS43OCAxMTEuNDMgMTQxLjQ5IDExOC4xNEMxNDYuMjQgMTIxLjMxIDE1MSAxMjQuNDggMTU1LjczIDEyNy42N0wxNTYuOTcgMTI2LjA0QzE2NC4xNiAxMTYuNTggMTc3LjExIDEyMS43IDE4NC40NSAxMjguMjFDMTg5LjE4IDEzMi40IDE5Mi45OCAxMzcuODkgMTk1LjMgMTQzLjc1QzE5OS44OCAxNTUuNCAxOTguNTUgMTczLjQ2IDE4Mi42NyAxNzIuNzNDMTY2LjU0IDE3Mi4wMSAxNTMuMDIgMTUyLjExIDE1My41MiAxMzYuOTJMMTUzLjU2IDEzNS43QzE0OC43IDEzMi41MSAxNDMuODggMTI5LjMgMTM5LjA0IDEyNi4wOUMxMzUuNDggMTMxLjE1IDEzMi42OCAxMzIuNDkgMTI1Ljg3IDEzMy42MkMxMjguMyAxNDguMDMgMTMwLjYgMTYyLjQ2IDEzMi45NyAxNzYuODhDMTUyLjM1IDE4OS41NSAxNzEuNjkgMjAyLjIzIDE5MS4xIDIxNC44MkMyMDMuNzcgMjAzLjI5IDIxNi4zNCAxOTEuNjQgMjI4Ljk3IDE4MC4wOUMyMjMuMzEgMTYwLjAxIDIxNy42OCAxMzkuOTEgMjEyLjAyIDExOS44MkMxOTUuNzkgMTA4LjkgMTc5LjU5IDk3LjkxOTkgMTYzLjM4IDg2Ljk1OTlWODYuOTM5OUwxNjMuMzkgODYuOTQ5OVoiIGZpbGw9IndoaXRlIi8+DQo8cGF0aCBmaWxsLXJ1bGU9ImV2ZW5vZGQiIGNsaXAtcnVsZT0iZXZlbm9kZCIgZD0iTTEwMy40NiAxMjIuOTFDOTguODYgMTI1LjU4IDk0LjIzIDEyOC4yMSA4OS42MSAxMzAuODNMOTAuMjIgMTMyLjU1Qzk2LjIxIDE0OS41OSA4Ny4wOSAxNjguNTIgNjcuNjMgMTY3LjcxQzUwLjc2IDE2Ny4wMSAzNi44MSAxNTEuNDMgMzYuMTggMTM0Ljg3QzM1LjU5IDExOS41MyA0Ni44OCAxMDcuNzggNjIuMzYgMTA5LjQxQzY5Ljc0IDExMC4xOSA3Ni41NSAxMTMuODYgODEuNzIgMTE5LjExTDgyLjg3IDEyMC4yOEM4Ny41MiAxMTcuNzggOTIuMTggMTE1LjI3IDk2Ljg3IDExMi44MUM5NC41NSAxMDYuMyA5My43NyAxMDIuMzkgOTUuMTYgOTUuNTYwMUM4My41MyA4Ny44ODAxIDcxLjg3IDgwLjIyMDEgNjAuMjIgNzIuNTcwMUM0MS4wOCA4MC43NzAxIDIxLjk1IDg4Ljk4MDEgMi44Mjk5NiA5Ny4yMTAxVjE3MS42NkMyNS40OSAxODYuMDMgNDguMTMgMjAwLjQgNzAuODUgMjE0LjdDODkuMTUgMjAxLjg5IDEwNy4zNiAxODguOTYgMTI1LjY1IDE3Ni4xM0MxMjMuNSAxNjEuNjggMTIxLjMxIDE0Ny4yNiAxMTkuMjIgMTMyLjgxQzExMi4yOCAxMzAuNjQgMTA4LjUgMTI3Ljk5IDEwMy40NiAxMjIuOTNWMTIyLjkxWiIgZmlsbD0id2hpdGUiLz4NCjxwYXRoIGZpbGwtcnVsZT0iZXZlbm9kZCIgY2xpcC1ydWxlPSJldmVub2RkIiBkPSJNMTAyLjY4IDEuMDYwMDZDODguOSA1LjM4MDA2IDc1LjE0IDkuNzQwMDYgNjEuMzcgMTQuMDQwMUM2Mi45MSAzMS4wMzAxIDY0LjQ3IDQ3Ljk5MDEgNjUuOTkgNjQuOTgwMUM3Ni42NSA3Mi4wNTAxIDg3LjMxIDc5LjIwMDEgOTguMDMgODYuMTgwMUMxMDAuNzYgODMuMDYwMSAxMDQuNTQgODEuMDEwMSAxMDguOTkgODAuMzYwMUMxMDguMzYgNzYuMjYwMSAxMDcuNzcgNzIuMjIwMSAxMDcuMTUgNjguMjEwMUM5Ni4yNiA2My43NDAxIDg3LjI5IDUzLjIzMDEgODUuOTQgNDIuNDUwMUM4NC4zOCAyOS45NzAxIDkzLjc3IDIyLjM1MDEgMTA2LjU0IDI0LjkxMDFDMTE4LjkzIDI3LjM4MDEgMTMwLjYxIDM4Ljc0MDEgMTMyLjk1IDUwLjc4MDFDMTM0Ljk1IDYxLjEzMDEgMTI5LjU0IDY4LjkzMDEgMTIwLjA4IDcwLjIxMDFDMTIwLjc3IDc0LjE2MDEgMTIxLjQzIDc4LjE3MDEgMTIyLjEyIDgyLjIzMDFDMTI2Ljc5IDg0LjEwMDEgMTMxLjE1IDg3LjIyMDEgMTM0Ljc5IDkxLjA2MDFDMTQyLjQzIDg2Ljk4MDEgMTUwLjA1IDgyLjg2MDEgMTU3LjY5IDc4Ljc3MDFDMTU0LjE5IDYyLjk1MDEgMTUwLjY2IDQ3LjE1MDEgMTQ3LjE2IDMxLjM1MDFDMTMyLjMzIDIxLjI4MDEgMTE3LjUxIDExLjIxMDEgMTAyLjcxIDEuMTEwMDZMMTAyLjY5IDEuMDcwMDZIMTAyLjY4VjEuMDYwMDZaIiBmaWxsPSJ3aGl0ZSIvPg0KPHBhdGggZmlsbC1ydWxlPSJldmVub2RkIiBjbGlwLXJ1bGU9ImV2ZW5vZGQiIGQ9Ik0xNjQuMyA4OC4xNjk5QzE1Ni43MyA5Mi40NDk5IDE0OS4xMyA5Ni43MTk5IDE0MS41OCAxMDEuMDZDMTQzLjcxIDEwNS41NyAxNDQuOTUgMTEwLjY1IDE0NC43MyAxMTUuNjVDMTQ4LjU3IDExOC4yMSAxNTIuNDEgMTIwLjc3IDE1Ni4yNSAxMjMuMzVDMTU5LjU1IDExOS4wMSAxNjQuNzEgMTE2Ljc1IDE3MC45NCAxMTcuNDVDMTgzLjMzIDExOC44IDE5Ni4wNyAxMzEuNDUgMTk5Ljg1IDE0Ni4yNUMyMDMuOCAxNjEuNjggMTk2LjcyIDE3NC4xNiAxODMuNTkgMTczLjU2QzE2Ni4zOCAxNzIuNzggMTUxLjgzIDE1Mi4wNSAxNTIuMzYgMTM1LjUxQzE0OC40MSAxMzIuOTEgMTQ0LjQ0IDEzMC4yOCAxNDAuNTEgMTI3LjY3QzEzNy44NCAxMzEuMDEgMTM0LjAyIDEzMy4zMSAxMjkuNCAxMzQuMDdDMTMxLjY2IDE0Ny40NCAxMzMuNzggMTYwLjg1IDEzNS45OCAxNzQuMjRDMTU0LjYyIDE4Ni40IDE3My4yMiAxOTguNjIgMTkxLjkxIDIxMC43M0MyMDMuODMgMTk5Ljg4IDIxNS42NSAxODguOTIgMjI3LjU1IDE3OC4wNEMyMjIuMSAxNTguNjQgMjE2LjY1IDEzOS4yMyAyMTEuMTggMTE5LjgzQzE5NS41NSAxMDkuMzIgMTc5LjkzIDk4LjcyOTkgMTY0LjMyIDg4LjE3OTlIMTY0LjNWODguMTY5OVoiIGZpbGw9IndoaXRlIi8+DQo8cGF0aCBmaWxsLXJ1bGU9ImV2ZW5vZGQiIGNsaXAtcnVsZT0iZXZlbm9kZCIgZD0iTTEwNC4xMSAxMjQuM0MxMDAuNTUgMTI2LjM2IDk2Ljk1IDEyOC40MiA5My4yOCAxMzAuNDlDOTkuNyAxNDguNzcgODkuODEgMTY5LjQ1IDY4LjU0IDE2OC41NEM1MC45OCAxNjcuODIgMzUuNyAxNTEuOCAzNC45OSAxMzMuNkMzNC4zMiAxMTYuMjYgNDcuMzggMTA0LjE5IDYzLjU4IDEwNS45QzcxLjQxIDEwNi43MyA3OC42OSAxMTAuNTkgODQuMjYgMTE2LjIzQzg3Ljk1IDExNC4yNSA5MS41NyAxMTIuMyA5NS4xNiAxMTAuNDFDOTMuNDUgMTA1LjYxIDkyLjY4IDEwMC4yNSA5My43MSA5NS4yMTk5QzgyLjgyIDg4LjAyOTkgNzEuOSA4MC44Njk5IDYxIDczLjY4OTlDNDIuNjYgODEuNTI5OSAyNC4zMiA4OS40MDk5IDYgOTcuMjk5OVYxNjkuMTJDMjcuOSAxODMuMDEgNDkuODIgMTk2Ljk0IDcxLjc3IDIxMC43M0M4OS4zMSAxOTguNDUgMTA2Ljc2IDE4Ni4wNyAxMjQuMjkgMTczLjc3QzEyMi4yNyAxNjAuMjMgMTIwLjIxIDE0Ni43IDExOC4yNiAxMzMuMTRDMTEzLjA5IDEzMS41MyAxMDguMTkgMTI4LjM5IDEwNC4xMSAxMjQuMjlWMTI0LjNaIiBmaWxsPSJ3aGl0ZSIvPg0KPHBhdGggZmlsbC1ydWxlPSJldmVub2RkIiBjbGlwLXJ1bGU9ImV2ZW5vZGQiIGQ9Ik05Ny40MTAxIDkwLjM2MDFDODUuODQwMSA4Mi44NTAxIDc0LjM4MDEgNzUuMTcwMSA2Mi44ODAxIDY3LjU1MDFDNjEuMjcwMSA0OS42NDAxIDU5LjYzMDEgMzEuNzIwMSA1OC4wMDAxIDEzLjgxMDFMNTUuMTgwMSAxNy40MTAxQzU2LjgxMDEgMzUuMzIwMSA1OC40NDAxIDUzLjI0MDEgNjAuMDYwMSA3MS4xNTAxQzcxLjU2MDEgNzguNzcwMSA4My4wMjAxIDg2LjQ1MDEgOTQuNTkgOTMuOTYwMUM5OC45MSA4OS4wMTAxIDEwMS4wNiA4OC4wODAxIDEwNy42MyA4Ny4xMjAxTDExMC40NSA4My41MjAxQzEwNC4xNiA4NC40NTAxIDEwMS44MyA4NS4zMjAxIDk3LjQxMDEgOTAuMzYwMVoiIGZpbGw9IndoaXRlIi8+DQo8cGF0aCBmaWxsLXJ1bGU9ImV2ZW5vZGQiIGNsaXAtcnVsZT0iZXZlbm9kZCIgZD0iTTEwNS4xMSAyOC4zN0M5Ny42IDI2Ljg3IDkxLjA5IDI5LjI0IDg4LjMzIDM0LjcxQzkxLjY3IDMxLjg1IDk2LjcxIDMwLjgzIDEwMi4yOSAzMS45NUMxMTMuNTYgMzQuMTkgMTI0Ljc2IDQ0LjcxIDEyNi45OSA1Ni4wOUMxMjcuNzMgNTkuODkgMTI3LjM0IDYzLjQ3IDEyNS45IDY2LjM2QzEyOS41NSA2My4yMyAxMzAuOTEgNTguMTMgMTI5LjgxIDUyLjUxQzEyNy42IDQxLjEyIDExNi40IDMwLjYzIDEwNS4xMSAyOC4zN1oiIGZpbGw9IndoaXRlIi8+DQo8cGF0aCBmaWxsLXJ1bGU9ImV2ZW5vZGQiIGNsaXAtcnVsZT0iZXZlbm9kZCIgZD0iTTExOS4xNyA4NS4xMDk5QzExOC4zMiA3OS45NDk5IDExNy40NSA3NC43OTk5IDExNi41NiA2OS42NTk5TDExMy43NCA3My4yNTk5QzExNC42MyA3OC40MTk5IDExNS41IDgzLjU0OTkgMTE2LjM1IDg4LjcwOTlDMTIyLjUyIDkxLjE4OTkgMTI1Ljk2IDkzLjg0OTkgMTMwLjUyIDk4LjY2OTlDMTM5LjE0IDk0LjA2OTkgMTQ3Ljc1IDg5LjQzOTkgMTU2LjM1IDg0Ljc5OTlMMTU5LjE3IDgxLjE5OTlDMTUwLjU3IDg1LjgzOTkgMTQxLjk2IDkwLjQ2OTkgMTMzLjM0IDk1LjA2OTlDMTI4Ljc4IDkwLjI0OTkgMTI1LjMzIDg3LjU3OTkgMTE5LjE3IDg1LjEwOTlaIiBmaWxsPSJ3aGl0ZSIvPg0KPHBhdGggZmlsbC1ydWxlPSJldmVub2RkIiBjbGlwLXJ1bGU9ImV2ZW5vZGQiIGQ9Ik0yLjgyIDE3MS42NFY5Ny4xODk5TDAgMTAwLjc5VjE3NS4yNEMyMi42NiAxODkuNjEgNDUuMyAyMDMuOTggNjguMDIgMjE4LjI4Qzg2LjMyIDIwNS40NyAxMDQuNTMgMTkyLjU0IDEyMi44MiAxNzkuNzFMMTI1LjY0IDE3Ni4xMUMxMDguMzggMTg4LjI0IDg4LjEzIDIwMi41NyA3MC44NCAyMTQuNjhDNDguMTIgMjAwLjQgMjUuNDggMTg2LjAxIDIuODIgMTcxLjY0WiIgZmlsbD0id2hpdGUiLz4NCjxwYXRoIGZpbGwtcnVsZT0iZXZlbm9kZCIgY2xpcC1ydWxlPSJldmVub2RkIiBkPSJNODEuNzMwMSAxMTkuMUM3Ni41NjAxIDExMy44NyA2OS43MzAxIDExMC4xOCA2Mi4zNzAxIDEwOS40QzUyLjYzMDEgMTA4LjM4IDQ0LjU3MDEgMTEyLjYzIDQwLjA2MDEgMTE5LjczQzQ0Ljc1MDEgMTE0LjgzIDUxLjU5MDEgMTEyLjE1IDU5LjU1MDEgMTEzQzY2LjkzMDEgMTEzLjc4IDczLjc0MDEgMTE3LjQ1IDc4LjkxMDEgMTIyLjdMODAuMDYwMSAxMjMuODdDODQuNzEwMSAxMjEuMzcgODkuMzcwMSAxMTguODYgOTQuMDYwMSAxMTYuNEw5Ni44ODAxIDExMi44QzkzLjM2MDEgMTE0LjY3IDg2LjQwMDEgMTE4LjM4IDgyLjg4MDEgMTIwLjI3TDgxLjczMDEgMTE5LjFaIiBmaWxsPSJ3aGl0ZSIvPg0KPHBhdGggZmlsbC1ydWxlPSJldmVub2RkIiBjbGlwLXJ1bGU9ImV2ZW5vZGQiIGQ9Ik04OS42MSAxMzAuODRMODYuNzkgMTM0LjQ0Qzg5Ljk2IDE0My40NyA5MC41NSAxNTIuNzEgODUuMjEgMTYxLjEyQzkxLjg1IDE1NC4yNCA5My44OSAxNDMuMDQgOTAuMiAxMzIuNTZMODkuNTkgMTMwLjg0SDg5LjYxWiIgZmlsbD0id2hpdGUiLz4NCjxwYXRoIGZpbGwtcnVsZT0iZXZlbm9kZCIgY2xpcC1ydWxlPSJldmVub2RkIiBkPSJNMTMyLjk2IDE3Ni44N0MxMzAuNzUgMTYzLjQzIDEyOC4xMiAxNDcuMDUgMTI1Ljg2IDEzMy42MUwxMjMuMDQgMTM3LjIxQzEyNS40NyAxNTEuNjIgMTI3Ljc3IDE2Ni4wNSAxMzAuMTQgMTgwLjQ3QzE0OS41MiAxOTMuMTQgMTY4Ljg2IDIwNS44MiAxODguMjcgMjE4LjQxQzIwMC45NCAyMDYuODggMjEzLjUxIDE5NS4yMyAyMjYuMTQgMTgzLjY4TDIyOC45NiAxODAuMDhDMjE3LjM1IDE5MC43MiAyMDIuNzQgMjA0LjIxIDE5MS4wOSAyMTQuODFDMTcxLjY4IDIwMi4yMiAxNTIuMzIgMTg5LjU0IDEzMi45NiAxNzYuODdaIiBmaWxsPSJ3aGl0ZSIvPg0KPHBhdGggZmlsbC1ydWxlPSJldmVub2RkIiBjbGlwLXJ1bGU9ImV2ZW5vZGQiIGQ9Ik0xMzcuNzUgMTAxLjUzTDEzNC45MyAxMDUuMTNDMTM3Ljc5IDExMS4xOCAxMzguOTcgMTE1LjAzIDEzOC42OCAxMjEuNzRDMTQzLjQzIDEyNC45MSAxNDguMTkgMTI4LjA4IDE1Mi45MiAxMzEuMjdMMTU1Ljc0IDEyNy42N0MxNTAuOTkgMTI0LjQ4IDE0Ni4yNSAxMjEuMzEgMTQxLjUgMTE4LjE0QzE0MS44IDExMS40OCAxNDAuNTQgMTA3LjQyIDEzNy43NSAxMDEuNTNaIiBmaWxsPSJ3aGl0ZSIvPg0KPHBhdGggZmlsbC1ydWxlPSJldmVub2RkIiBjbGlwLXJ1bGU9ImV2ZW5vZGQiIGQ9Ik0xODQuNDYgMTI4LjE5QzE3Ny4xNCAxMjEuNjggMTY0LjE3IDExNi41NiAxNTYuOTggMTI2LjAyTDE1NS43NCAxMjcuNjVMMTU0LjkyIDEyOC42OUMxNjIuMjQgMTIwLjUxIDE3NC41NiAxMjUuNSAxODEuNjQgMTMxLjc5QzE4Ni4zNyAxMzUuOTggMTkwLjE3IDE0MS40NyAxOTIuNDkgMTQ3LjMzQzE5NS4yMiAxNTQuMjggMTk1Ljg1IDE2My41MiAxOTIuNTMgMTY5LjY5QzE5OC44MyAxNjQuMTUgMTk4LjY1IDE1Mi4yNCAxOTUuMzEgMTQzLjc1QzE5My4wMSAxMzcuODcgMTg5LjE5IDEzMi40IDE4NC40NiAxMjguMjFWMTI4LjE5WiIgZmlsbD0id2hpdGUiLz4NCjxwYXRoIGZpbGwtcnVsZT0iZXZlbm9kZCIgY2xpcC1ydWxlPSJldmVub2RkIiBkPSJNMTA2LjU2IDI0Ljg5MDFDOTMuNzggMjIuMzMwMSA4NC4zOCAyOS45NzAxIDg1Ljk2IDQyLjQzMDFDODcuMzEgNTMuMTkwMSA5Ni4zMSA2My43MjAxIDEwNy4xNyA2OC4xOTAxTDEwOC4xMyA2Ny45OTAxTDEwNi45OCA2Ny41MzAxQzk3LjQxIDYzLjYwMDEgODguNDIgNTQuMDEwMSA4Ny4xIDQzLjUzMDFDODUuNjUgMzIuMDMwMSA5NC4zNSAyNi4yNTAxIDEwNS4xMiAyOC40MDAxQzExNi4zOSAzMC42NDAxIDEyNy41OSA0MS4xNjAxIDEyOS44MiA1Mi41NDAxQzEzMS40NSA2MC45NDAxIDEyNy42MyA2OC4yMTAxIDExOC44MSA2OS40MDAxTDExNi41NyA2OS43MDAxTDEyMC4xMSA3MC4yMjAxQzEyOS41NyA2OC45NjAxIDEzNC45OCA2MS4xMzAxIDEzMi45OCA1MC43OTAxQzEzMC42NCAzOC43NzAxIDExOC45NiAyNy4zOTAxIDEwNi41NyAyNC45MjAxTDEwNi41NSAyNC45MDAxSDEwNi41NlYyNC44OTAxWiIgZmlsbD0id2hpdGUiLz4NCjxwYXRoIGZpbGwtcnVsZT0iZXZlbm9kZCIgY2xpcC1ydWxlPSJldmVub2RkIiBkPSJNMTcwLjk0IDExNy40NUMxNjQuNzEgMTE2Ljc2IDE1OS41NSAxMTkuMDMgMTU2LjI1IDEyMy4zNUwxNTUuNzMgMTI3LjY1TDE1Ni45NyAxMjYuMDJDMTY0LjE2IDExNi41NiAxNzcuMTEgMTIxLjY4IDE4NC40NSAxMjguMTlDMTg5LjE4IDEzMi4zOCAxOTIuOTggMTM3Ljg3IDE5NS4zIDE0My43M0MxOTkuODggMTU1LjM4IDE5OC41NSAxNzMuNDQgMTgyLjY3IDE3Mi43MUMxNjYuNTQgMTcxLjk5IDE1My4wMiAxNTIuMDkgMTUzLjUyIDEzNi45TDE1My41NiAxMzUuNjhMMTUyLjM1IDEzNS40OUMxNTEuODEgMTUyLjAzIDE2Ni4zNyAxNzIuNzggMTgzLjU4IDE3My41NEMxOTYuNzMgMTc0LjEzIDIwMy43OSAxNjEuNjUgMTk5Ljg0IDE0Ni4yM0MxOTYuMDYgMTMxLjQzIDE4My4zMiAxMTguNzkgMTcwLjkzIDExNy40M1YxMTcuNDVIMTcwLjk0WiIgZmlsbD0id2hpdGUiLz4NCjxwYXRoIGZpbGwtcnVsZT0iZXZlbm9kZCIgY2xpcC1ydWxlPSJldmVub2RkIiBkPSJNODkuNjEwMSAxMzAuODRMOTAuMjIwMSAxMzIuNTZDOTYuMjEwMSAxNDkuNiA4Ny4wOSAxNjguNTMgNjcuNjMgMTY3LjcyQzUwLjc2IDE2Ny4wMiAzNi44MSAxNTEuNDQgMzYuMTggMTM0Ljg4QzM1LjU5IDExOS41NCA0Ni44OCAxMDcuNzkgNjIuMzYgMTA5LjQyQzY5Ljc0IDExMC4yIDc2LjU1MDEgMTEzLjg3IDgxLjcyMDEgMTE5LjEyTDgyLjg3MDEgMTIwLjI5TDg0LjI4IDExNi4yNUM3OC43IDExMC42MSA3MS40MSAxMDYuNzYgNjMuNiAxMDUuOTJDNDcuMzkgMTA0LjIxIDM0LjM0IDExNi4yOSAzNS4wMSAxMzMuNjJDMzUuNzEgMTUxLjgzIDUwLjk5IDE2Ny44NSA2OC41NiAxNjguNTZDODkuODEgMTY5LjQ3IDk5LjcxMDEgMTQ4Ljc5IDkzLjMwMDEgMTMwLjUxTDg5LjYzIDEzMC44Nkw4OS42MTAxIDEzMC44NFoiIGZpbGw9IndoaXRlIi8+DQo8L2c+DQo8ZGVmcz4NCjxjbGlwUGF0aCBpZD0iY2xpcDBfMTczXzIiPg0KPHJlY3Qgd2lkdGg9IjIyOC45NyIgaGVpZ2h0PSIyMTguNDEiIGZpbGw9IndoaXRlIi8+DQo8L2NsaXBQYXRoPg0KPC9kZWZzPg0KPC9zdmc+DQo=";

	#endregion

	#region Fields: Private

	private IComposableApplicationManager _sut;
        private readonly string workspacesFolderPath = Path.Combine(Path.GetTempPath(), "workspaces");

	#endregion

	#region Methods: Protected

	protected override void AdditionalRegistrations(ContainerBuilder containerBuilder){
		base.AdditionalRegistrations(containerBuilder);
		var logger = Substitute.For<ILogger>();
		//containerBuilder.RegisterType<ZipFileMockWrapper>().As<IZipFile>();
		containerBuilder
			.RegisterInstance(
				new ZipFileMockWrapper(FileSystem, new WorkingDirectoriesProvider(logger, new FileSystem())))
			.As<IZipFile>();
	}

	#endregion

	#region Methods: Public

	public override void Setup(){
		base.Setup();
               FileSystem.MockExamplesFolder("workspaces", Path.Combine(Path.GetTempPath(), "workspaces"));
               FileSystem.MockExamplesFolder("SVG_Icons", Path.Combine(Path.GetTempPath(), "SVG_Icons"));
               FileSystem.MockExamplesFolder("AppZips", Path.Combine(Path.GetTempPath(), "AppZips"));
		_sut = Container.Resolve<IComposableApplicationManager>();
	}

	#endregion

	[Test]
	public void SetIcon_ShouldSetCorrectFileNameAndIcon(){
		// Arrange
               const string expectedFilePath
                       = Path.Combine(Path.GetTempPath(), "workspaces", "ApolloAppWorkspace", "packages", "MrktApolloApp", "Files", "app-descriptor.json");
		const string appName = "MrktApolloApp";

		// Act
		_sut.SetIcon(workspacesFolderPath, IconPath, appName);

		// Assert
		FileSystem.FileExists(expectedFilePath);
		string appDescriptorContent = FileSystem.File.ReadAllText(expectedFilePath);

		AppDescriptorJson appDescriptor = JsonConvert.DeserializeObject<AppDescriptorJson>(appDescriptorContent);
		string iconFileName = Path.GetFileNameWithoutExtension(IconPath);
		string timestampPattern = @"\d{14}.svg$"; // Matches the datetime format "yyyyMMddHHmmss"
		appDescriptor.IconName.Should().MatchRegex($"{iconFileName}_{timestampPattern}");
		appDescriptor.Icon.Should().Be(PartnerSvgBase64);
	}

	[Test]
	public void GetAppCode() {
		// Arrange
		const string appName = "ApolloAppWorkspace";
		const string appCode = "MrktApolloApp";
		string workspacePath = Path.Combine(workspacesFolderPath, appName);
		// Act
		string actualAppCode = _sut.GetCode(workspacePath);

		// Assert

		actualAppCode.Should().NotBeNullOrEmpty();
		actualAppCode.Should().Be(appCode);
	}

	[Test]
	public void GetAppCode_ThrowException_IfAppDescriptorNotFound() {
		// Arrange
		const string appName = "iframe-sample";
		string workspacePath = Path.Combine(workspacesFolderPath, appName);

		// Act
		Action act = () => _sut.GetCode(workspacePath);

		// Assert
		act.Should().Throw<FileNotFoundException>()
			.WithMessage($"No app-descriptor.json file found in the specified workspace path. {workspacePath}");
	}

	

	[Test]
	public void SetIcon_ShouldSetCorrectIcon_WhenUsingZipArchive(){
		// Arrange
               string zipAppPath = Path.Combine(Path.GetTempPath(), "AppZips", "MrktApolloApp.zip");
               string unzipAppPath = Path.Combine(Path.GetTempPath(), "AppZips");

		// Act
		_sut.SetIcon(zipAppPath, IconPath, string.Empty);

		// Assert
		FileSystem.FileExists(zipAppPath);
		Container
			.Resolve<IPackageArchiver>()
			.ExtractPackages(zipAppPath, true, true, true, false, unzipAppPath);
		string appDescriptorContent
			= FileSystem.File.ReadAllText(Path.Combine(unzipAppPath, "MrktApolloApp", "Files", "app-descriptor.json"));
		AppDescriptorJson appDescriptor = JsonConvert.DeserializeObject<AppDescriptorJson>(appDescriptorContent);
		string iconFileName = Path.GetFileNameWithoutExtension(IconPath);
		string timestampPattern = @"\d{14}.svg$";
		appDescriptor.IconName.Should().MatchRegex($"{iconFileName}_{timestampPattern}");
		appDescriptor.Icon.Should().Be(PartnerSvgBase64);
	}

	[Test]
	public void SetIcon_ShouldThrow_When_AppDescriptorDoesNotExist(){
		// Arrange
		const string appName = "iframe-sample";

		string packagesFolderPath = Path.Combine(workspacesFolderPath, "iframe-sample");
		// Act
		Action act = () => _sut.SetIcon(packagesFolderPath, IconPath, appName);

		// Assert
		act.Should().Throw<FileNotFoundException>()
			.WithMessage(
				$"No app-descriptor.json file found in the specified packages folder path. {packagesFolderPath}");
	}

	[Test]
	public void SetIcon_ShouldThrow_When_AppNotFound(){
		// Arrange
		const string appName = "NonExistingApp";

		// Act
		Action act = () => _sut.SetIcon(workspacesFolderPath, IconPath, appName);

		// Assert
		act.Should().Throw<ValidationException>()
			.WithMessage($"App {appName} not found.");
	}

	[Test]
	public void SetIcon_ShouldThrow_When_AppPathIsEmpty(){
		// Arrange
		const string appName = "MyAppCode";

		// Act
		Action act = () => _sut.SetIcon(string.Empty, IconPath, appName);

		// Assert
		act.Should().Throw<ValidationException>()
			.WithMessage($"Validation failed: {Environment.NewLine} -- AppPath: App path is required. Severity: Error");
	}

	[Test]
	public void SetIcon_ShouldThrow_When_IconPathIsEmpty(){
		// Arrange
		const string appName = "MyAppCode";

		// Act
		Action act = () => _sut.SetIcon(workspacesFolderPath, string.Empty, appName);

		// Assert
		act.Should().Throw<ValidationException>()
			.WithMessage(
				$"Validation failed: {Environment.NewLine} -- IconPath: Icon path is required. Severity: Error");
	}

	[Test]
	public void SetIcon_ShouldThrow_When_IconPathNonExistant(){
		// Arrange
		const string appName = "MyAppCode";

		// Act
               Action act = () => _sut.SetIcon(workspacesFolderPath, Path.Combine(Path.GetTempPath(), "1.svg"), appName);

		// Assert
		act.Should().Throw<ValidationException>()
			.WithMessage(
                               $"Validation failed: {Environment.NewLine} -- IconPath: Icon file '{Path.Combine(Path.GetTempPath(), "1.svg")}' must exist. Severity: Error");
	}

	[Test]
	public void SetIcon_ShouldThrow_When_MultipleAppDescriptorFoundWithAppCode(){
		// Arrange
		const string appName = "MyAppCode";

		// Act
		Action act = () => _sut.SetIcon(workspacesFolderPath, IconPath, appName);

		// Assert
               act.Should().Throw<InvalidOperationException>()
                       .WithMessage("More than one app-descriptor.json file found with the same Code:\n" +
                               Path.Combine(Path.GetTempPath(), "workspaces", "MyAppV1", "packages", "MrktApolloApp", "Files", "app-descriptor.json") + "\n" +
                               Path.Combine(Path.GetTempPath(), "workspaces", "MyAppV2", "packages", "MrktApolloApp", "Files", "app-descriptor.json") + "\n");
	}

	[Test]
	public void SetIcon_ShouldThrow_When_PackagesFolderPathNonExistant(){
		// Arrange
		const string appName = "MyAppCode";

		// Act
               Action act = () => _sut.SetIcon(Path.Combine(Path.GetTempPath(), "NonRealDir"), IconPath, appName);

		// Assert
		act.Should().Throw<ValidationException>()
			.WithMessage(
                               $"Validation failed: {Environment.NewLine} -- AppPath: Path '{Path.Combine(Path.GetTempPath(), "NonRealDir")}' must exist as a directory or a file. Severity: Error");
	}

}

public class ZipFileMockWrapper : IZipFile
{

	#region Fields: Private

	private readonly MockFileSystem _fileSystem;
	private readonly IWorkingDirectoriesProvider _workingDirectoriesProvider;

	#endregion

	#region Constructors: Public

	public ZipFileMockWrapper(MockFileSystem fileSystem, IWorkingDirectoriesProvider workingDirectoriesProvider){
		_fileSystem = fileSystem;
		_workingDirectoriesProvider = workingDirectoriesProvider;
	}

	#endregion

	#region Methods: Public

	public void CreateFromDirectory(string sourceDirectoryName, string destinationArchiveFileName){
		List<string> allFiles = _fileSystem.Directory
			.GetFiles(sourceDirectoryName, "*", SearchOption.AllDirectories)
			.ToList();

		_workingDirectoriesProvider.CreateTempDirectory(tempDir => {
			foreach (string file in allFiles) {
				
				string relativePathFileName =file.Replace(sourceDirectoryName, string.Empty); 
				string realFsFileName =  Path.Combine(tempDir, relativePathFileName.TrimStart('\\'));
				Directory.CreateDirectory(Path.GetDirectoryName(realFsFileName));
				byte[] fakeFsBytes = _fileSystem.File.ReadAllBytes(file);
				File.WriteAllBytes(realFsFileName, fakeFsBytes); //write to real FS
			}

			_workingDirectoriesProvider.CreateTempDirectory(realFsSrcPath => {
				string fileName = Path.Combine(realFsSrcPath, Path.GetFileName(destinationArchiveFileName));
				ZipFile.CreateFromDirectory(tempDir, fileName);
				byte[] fakeFsBytes = System.IO.File.ReadAllBytes(fileName);
				
				_fileSystem.Directory.CreateDirectory(Path.GetDirectoryName(fileName));
				_fileSystem.File.WriteAllBytes(destinationArchiveFileName, fakeFsBytes); 
			});
		});
	}

	public void ExtractToDirectory(string sourceArchiveFileName, string destinationDirectoryName){
		_workingDirectoriesProvider.CreateTempDirectory(tempDir => {
			_workingDirectoriesProvider.CreateTempDirectory(realFsSrcPath => {
				byte[] fakeFsBytes = _fileSystem.File.ReadAllBytes(sourceArchiveFileName);
				string fileName = Path.Combine(realFsSrcPath, Path.GetFileName(sourceArchiveFileName));
				File.WriteAllBytes(fileName, fakeFsBytes); //write to real FS
				ZipFile.ExtractToDirectory(fileName, tempDir); //write to real FS
				_fileSystem.MockFolder(tempDir, destinationDirectoryName);
			});
		});
	}

	#endregion

}