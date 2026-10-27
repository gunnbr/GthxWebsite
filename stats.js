
function processSearchResults(reply, textStatus, jqXHR) {
    if (reply["status"] != "success")
    {
        alert("Search failure: " + reply["reason"]);
        return;
    }
    $(".loading").remove();
    $("#factoidResults tr").remove();
    var table=$("#factoidResults");
    var rows=reply["refs"];
    for (index in rows)
    {
	var row = rows[index];

	table.append("<tr>" +
		     "<td>" + row["item"]  + "</td>" +
                     "<td>" + row["count"] + "</td>" +
		     "</tr>");
    }
    $("#factoidResults").trigger("update");

    $("#thingiverseResults tr").remove();
    var table=$("#thingiverseResults");
    var rows=reply["thingiverseRefs"];
    for (index in rows)
    {
	var row = rows[index];

	table.append("<tr>" +
		     "<td>" + "<a href=\"https://www.thingiverse.com/thing:" + row["item"]  + "\">" + row["item"] + "</a></td>" +
                     "<td>" + row["count"] + "</td>" +
		     "</tr>");
    }
    $("#factoidResults").trigger("update");
    
}
    
function initialize() {
    $("#factoidTable").tablesorter();
    $("#thingiverseTable").tablesorter();

    $(".loading").show();
    var request = new Object;
    $.getJSON("cgi-bin/stats-relay.py", request, processSearchResults);
}

$().ready(initialize);
