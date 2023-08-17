clio createw %1
git init
git add .
git commit -m "init project workspace"
git branch -M master
git remote add origin %2
git push -u origin master