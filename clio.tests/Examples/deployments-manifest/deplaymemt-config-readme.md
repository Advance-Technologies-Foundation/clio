I have separate repository for environmnent which contains conf.yaml file

PreprodRepo
    preprod-conf.yaml


If I want Add new composable application to environment i need add it to conf.yaml in section apps

apps
  - name: CrtCustomer360
    version: 1.0.1
    app-hub: MyAppHub

If I want delete composable application from environment i need delete it from conf.yaml in section apps

On this repository I have Jenkins pipeline which is triggered by webhook from PreprodRepo. And run clio to made changes 
on the environment defined in the conf.yaml file.

clio configure-environment preprod-conf.yaml

This approach provide professional change managment process for your environments. Use it when your need organize your
infrastructure and development process in the way that you can easily track changes and have full control over them.