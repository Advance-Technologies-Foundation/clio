# DB Name length bug

Postgres database has a limit 63 characters per database name.

The current implementation of `restore-db` command and `deploy-creatio` command do not account for this limitation.

This causes a bug when I provide a zip file to `deploy-creatio` from that will create a template_ db and the real db longer than 63 characters.

Current implementation takes the original zip file and creates a template db with the name `template_zipfilename_without_extension` and then creates the real db with the name `zip_filename_without_extension`.

This may exceed the 63 character limit.

I want to clio to create a template db with a name never exceeding 63 charactes, for instance it can be `template_GUID`. 
I want this template to keep information about the original zip file in its comments, and when the restore is performed, we search for a template not by name but rather by comments.
