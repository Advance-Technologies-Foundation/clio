	<!-- Reference to #dll-name-here# -->
	<Choose>
		<!--Used when dll already exists in Core-Lib folder-->
		<When Condition="Exists('$(CoreLibPath)/#dll-name-here#.dll')">
			<ItemGroup>
				<Reference Include="#dll-name-here#">
					<HintPath>$(CoreLibPath)/#dll-name-here#.dll</HintPath>
					<SpecificVersion>False</SpecificVersion>
					<Private>False</Private>
				</Reference>
			</ItemGroup>
		</When>
		
		<!-- Used when building for NetFramework-->
		<When Condition="Exists('Libs/net472/#dll-name-here#.dll') and '$(TargetFramework)' == 'net472'">
			<ItemGroup>
				<Reference Include="#dll-name-here#">
					<HintPath>Libs/net472/#dll-name-here#.dll</HintPath>
					<SpecificVersion>False</SpecificVersion>
					<Private>False</Private>
				</Reference>
			</ItemGroup>
		</When>
		
		<!-- Used when building for netstandrad 2.0-->
		<When Condition="Exists('Libs/netstandard/#dll-name-here#.dll') and '$(TargetFramework)' == 'netstandard2.0'">
			<ItemGroup>
				<Reference Include="#dll-name-here#">
					<HintPath>Libs/netstandard/#dll-name-here#.dll</HintPath>
					<SpecificVersion>False</SpecificVersion>
					<Private>False</Private>
				</Reference>
			</ItemGroup>
		</When>
	</Choose>