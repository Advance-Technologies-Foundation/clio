clio cfg -a %1
git checkout -b %1
clio cfgw -e %1 -p %2
clio restorew -e %1