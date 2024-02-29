## Watch [YouTube Freedom UI tutorial]

## [Install clio]
```bash
dotnet tool update clio -g
```

## [Create clio environment]
```bash
clio reg-web-app <ENVIRONMENT_NAME> -u https://mysite.creatio.com -l administrator -p password
```
> Update ./.clio/workspaceEnvironmentSettings.json file with the name of the environment created in the previous step.
>```json
> {"Environment": "apollo-bundle-core"}
>``` 


## Install NPM dependencies
from ./projects/iframeintegration folder
```bash
npm install
```

## [Push workspace]
```bash
clio pushw -e <ENVIRONMENT_NAME>
```


<!-- Named links -->
[Create clio environment]:https://github.com/kirillkrylov/iframe-demo.git

[Install clio]: https://github.com/Advance-Technologies-Foundation/clio/blob/master/README.md#windows

[Push workspace]: https://github.com/Advance-Technologies-Foundation/clio/blob/master/README.md#windows

[YouTube Freedom UI tutorial]: https://youtu.be/IbYrd4QyMAY?si=Oev_YPi8XFYrNBe4