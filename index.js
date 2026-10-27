
function processSearchResults(reply, textStatus, jqXHR) {
    if (reply["status"] != "success")
    {
        alert("Search failure: " + reply["reason"]);
        return;
    }
    $(".loading").remove();
    $("#results tr").remove();
    var table=$("#results");
    var rows=reply["data"];
    for (index in rows)
    {
	var row = rows[index];

	if (row["nick"] == null)
	{
	    row["nick"] = "";
	}

	table.append("<tr>" +
		     "<td>" + row["item"]  + "</td>" +
                     "<td>" + row["value"] + "</td>" +
                     "<td>" + row["nick"] + "</td>" +
		     "</tr>");
    }
    $("#results").trigger("update");
}
    
function processSearch() {
    $(".loading").show();
    var request = new Object;
    request.searchstring = $("#searchstring").val();
    $.getJSON("cgi-bin/factoid-relay.py", request, processSearchResults);
}

function processKeypress(event) {
    if ( event.which == 13 ) {
	event.preventDefault();
	processSearch();
    }
}

function initialize() {
    $("#search").click(processSearch);
    $("#searchstring").keypress(processKeypress);
    $("#resultTable").tablesorter();
    $("#searchstring").focus();

    // Copied from stackoverflow
    var urlParams = {};
    var e,
    a = /\+/g,  // Regex for replacing addition symbol with a space
    r = /([^&=]+)=?([^&]*)/g,
    d = function (s) { return decodeURIComponent(s.replace(a, " ")); },
    q = window.location.search.substring(1);
    
    while (e = r.exec(q)) {
	var param = d(e[1]);
	var value = d(e[2]);

	if (param == "search")
	{
	    var request = new Object;
	    request.searchstring = value;
	    $.getJSON("cgi-bin/factoid-relay.py", request, processSearchResults);
	}
    }
}

$().ready(initialize);
