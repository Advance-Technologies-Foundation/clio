using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;

namespace Clio.Command.Administration;

public sealed partial class AdministrationService {
	private static readonly string[] IpColumns = ["Id", SysAdminUnitSchema, "BeginIP", "EndIP"];

	/// <inheritdoc />
	public JsonElement GetIpRanges(Guid unitId, int offset, int limit) {
		GetUnit(unitId);
		return client.Select(SysAdminUnitIPRangeSchema, IpColumns,
			new Dictionary<string, object> { [SysAdminUnitSchema] = unitId }, offset, limit);
	}

	/// <inheritdoc />
	public JsonElement SetIpRange(Guid id, Guid unitId, string beginIp, string endIp, bool create) {
		RequireId(id);
		GetUnit(unitId);
		IPAddress begin = ParseIpv4(beginIp);
		IPAddress end = ParseIpv4(endIp);
		byte[] startBytes = begin.GetAddressBytes();
		byte[] endBytes = end.GetAddressBytes();
		// Native login compares each octet independently, rather than comparing a 32-bit address.
		for (int index = 0; index < startBytes.Length; index++) {
			if (startBytes[index] > endBytes[index]) {
				throw new ArgumentException("Each beginning IPv4 octet must be at most the corresponding ending octet; split the range if needed.");
			}
		}
		Dictionary<string, object> filter = new() { ["Id"] = id };
		JsonElement rows = client.Select(SysAdminUnitIPRangeSchema, IpColumns, filter, limit: 2);
		RequireExpectedCount(rows, create ? 0 : 1);
		if (!create && LookupId(rows[0], SysAdminUnitSchema) != unitId) {
			throw new ArgumentException("The access rule belongs to a different user or role.");
		}
		client.WriteEntity(SysAdminUnitIPRangeSchema, id, new Dictionary<string, object> {
			[SysAdminUnitSchema] = unitId, ["BeginIP"] = begin.ToString(), ["EndIP"] = end.ToString()
		}, create);
		rows = client.Select(SysAdminUnitIPRangeSchema, IpColumns, filter, limit: 2);
		RequireExpectedCount(rows, 1);
		if (LookupId(rows[0], SysAdminUnitSchema) != unitId || rows[0].GetProperty("BeginIP").GetString() != begin.ToString()
			|| rows[0].GetProperty("EndIP").GetString() != end.ToString()) {
			throw new AdministrationStateException("IP access rule readback differs from the request.");
		}
		return rows[0].Clone();
	}

	/// <inheritdoc />
	public void DeleteIpRange(Guid id, Guid unitId) {
		RequireId(id);
		GetUnit(unitId);
		Dictionary<string, object> filter = new() { ["Id"] = id };
		JsonElement rows = client.Select(SysAdminUnitIPRangeSchema, IpColumns, filter, limit: 2);
		RequireExpectedCount(rows, 1);
		if (LookupId(rows[0], SysAdminUnitSchema) != unitId) { throw new ArgumentException("The access rule belongs to a different user or role."); }
		client.DeleteEntity(SysAdminUnitIPRangeSchema, id);
		RequireExpectedCount(client.Select(SysAdminUnitIPRangeSchema, IpColumns, filter, limit: 1), 0);
	}

	private static IPAddress ParseIpv4(string value) {
		if (!IPAddress.TryParse(value, out IPAddress address) || address.AddressFamily != AddressFamily.InterNetwork
			|| value != address.ToString()) {
			throw new ArgumentException("Use a canonical dotted IPv4 address. IPv6 writes are unavailable because affected Creatio versions fail mixed-family login checks.");
		}
		return address;
	}
}
