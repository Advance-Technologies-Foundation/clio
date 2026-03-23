# Docker Image

Creatio binaries are available in two main flavors
- .NET8
- .NET Framework

Currently .NET8 is the latest supported framework for Creatio, within months we will also support .NET10
Creatio can be installed on Linux and windows, and many customers are asking for Docker/Kubernetes support.
Clio is the main DevOps and Dev tool expected to alleviate the pain of building and maintaining docker images for Creatio. 


## Objective
Create a cli command to build a docker image for Creatio.

Creatio images will be used for different purposes, for example:
- Development and Testing, this is meant for local development and testing environments
- API Testing, this is meant for testing the API endpoints of the application. Images will be spun up within CI/CD pipelines and destroyed after the tests are complete.
- User acceptance testing, this is meant for staging environments where users can test the application before it goes to production.
- Production, this is meant for production environments where the application will be running.


## Expected capabilities of a command

CLIO should ship with some defaults way of building images, however in runtime a user should be able to select a custom template to build from.
Command should operate on a ZIP file or a directory containing the application, just like `clio deploy-creatio` does.


clio `build-docker-image` --from <path-to-zip-file_or_folder> --template <template-name>

Where:
- <path-to-zip-file> is the path to the ZIP file containing the application
- <template-name> is the name of the template to use for building the image (dev, prod, custom_template_name)

Optional arguments:
- --output-path: in some instances I may want to export  a built image to a local `tar` file, this is useful for air gapped environments where I cannot push images to a registry.
- --registry: the registry to push the image to, this is useful for production images that need to be pushed to a registry for deployment.


## Examples

I have a working prototype how I build docker image. review it in `C:\CreatioBuilds\Docker\build-docker.ps1` file. Only use it for reference.


## Where to keep templates

Clio should provide a sensible template for various builds (dev, prod). I want to keep them in a folder next to the 'infrastructure' folder
- appsettings.json
- schema.json
- infrastructure (folder)
- docker-templates (folder)
  - dev (folder)
  - prod (folder)
  - custom_1 (folder)
  - custom_2 (folder)



