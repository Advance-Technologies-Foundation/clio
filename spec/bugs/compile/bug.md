Exception when run
```pwsh
clio compile-remote --all -e bndl_n8 --debug
```

[ERR] - System.MissingMethodException: Constructor on type 'Clio.Command.CompileConfigurationCommand' not found.
at System.RuntimeType.CreateInstanceImpl(BindingFlags bindingAttr, Binder binder, Object[] args, CultureInfo culture)
at System.Activator.CreateInstance(Type type, Object[] args)
at Clio.Program.CreateRemoteCommand[TCommand](EnvironmentOptions options, Object[] additionalConstructorArgs)
at Clio.Program.<>c.<.cctor>b__93_0(Object instance)
at Clio.Program.ExecuteCommands(String[] args)
at Clio.Program.Main(String[] args)



