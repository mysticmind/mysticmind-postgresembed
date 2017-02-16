var jsonfile = require('jsonfile');

// Read in the file to be patched
var file = process.argv[2];
if (!file)
    console.log("No filename provided");
console.log("File: " + file);

// Read in the build version (this might be provided by the CI server)
var version = process.argv[3];
if (!version)
	console.log("No version provided");

jsonfile.readFile(file, function (err, project) {
    project.version = version;
    jsonfile.writeFile(file, project, { spaces: 2 }, function (err) {
        if (err)
            console.error(err);
        else
            console.log("Project version succesfully set.");
    });
})