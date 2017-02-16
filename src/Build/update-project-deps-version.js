var jsonfile = require('jsonfile');

// Read in the file to be patched
var file = process.argv[2];
if (!file)
    console.log("No filename provided");
console.log("File: " + file);

var dep = process.argv[3];
if (!dep)
    console.log("No dependency name provided");
console.log("Dep: " + dep);

// Read in the build version (this might be provided by the CI server)
var version = process.argv[4];
if (!version)
	console.log("No version provided");

jsonfile.readFile(file, function (err, project) {
    project.dependencies[dep] = version;
    jsonfile.writeFile(file, project, { spaces: 2 }, function (err) {
        if (err)
            console.error(err);
        else
            console.log("Project dep version " + dep + "=" + version + " succesfully set.");
    });
})