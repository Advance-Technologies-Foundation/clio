# Unlock Multiple packages
I want `unlock-package` command to take maintainer as console argument, and unlock all packages in an environment with that maintainer.

I do not want to alter mainter in a package. I want command to change system setting called `Maintainer` 
to take on a value of mainter Arg, then unlock all packages

my command should look like this
```bash
clio unlock-package -m Creatio -e dev_env_n8
```
